namespace GenWave.Context.History;

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Http;

/// <summary>
/// The F109 <see cref="IContextProvider"/>: 2-4 curated On-This-Day facts from Wikimedia's REST feed
/// (keyless, no API key ever touches this class), day-file cached under
/// <c>{CacheRoot}/context/history/{MM-dd}.json</c> so a short Wikimedia outage — or a restart — still
/// serves. <see cref="WikimediaBaseAddress"/> is a fixed host baked into the DI registration, never a
/// caller- or config-supplied URL (the same T221 review SSRF-safe framing <c>WeatherContextProvider</c>
/// follows).
///
/// <para>
/// <b><c>selected</c>, not <c>events</c> (T228 build-time decision).</b> Wikimedia's On-This-Day feed
/// exposes both <c>GET .../onthisday/selected/{MM}/{dd}</c> (curated, ~20 entries for a typical date —
/// verified against a real reply during T228 development) and <c>.../onthisday/events/{MM}/{dd}</c>
/// (every recorded event, 60+ entries for the same date). <c>selected</c> won: SPEC F109.1 wants "2-4
/// curated entries" for the segment lane, and Wikimedia has already done that curation — picking a
/// small, coherent subset out of the much larger <c>events</c> firehose is exactly the editorial work
/// <c>selected</c> exists to save every consumer from redoing.
/// </para>
///
/// <para>
/// <b>Real reply shape (curl'd from api.wikimedia.org during T228 development —
/// <c>curl -A "GenWave/dev (+https://github.com/GenWave-Org/genwave)"
/// https://api.wikimedia.org/feed/v1/wikipedia/en/onthisday/selected/08/08</c>):</b>
/// <code>
/// { "selected": [ { "text": "The World Health Organization declared ...", "year": 2014, "pages": [...] }, ... ] }
/// </code>
/// Each entry also carries a large <c>pages</c> array (thumbnails, full article extracts, wikibase
/// items) this class never deserializes at all (<see cref="WikimediaSelectedEvent"/>'s own remarks) —
/// only <c>text</c>/<c>year</c> feed the facts this provider produces.
/// </para>
///
/// <para>
/// <b>Fail-closed on the cache root (mirrors <c>WeatherContextProvider.TryParseCoordinates</c>'s own
/// posture one config knob over).</b> <see cref="IContextCacheRootProvider.Root"/> is read FRESH on
/// every <see cref="FetchAsync"/> call and validated before anything else runs: a blank root makes
/// this provider behave as if disabled — zero outbound requests, zero disk I/O, one
/// <see cref="LogLevel.Information"/> line naming the cause. This class also implements
/// <see cref="ISelfGatingContextProvider"/> (re-running the same check cheaply and synchronously),
/// which is what <see cref="ContextPipeline"/> actually calls in production — see that interface's own
/// remarks for why this method's own check stays only as the independent defense-in-depth backstop.
/// </para>
///
/// <para>
/// <b>Station-local date (mirrors <c>ScheduleResolver</c>'s own <c>IStationClockProvider?</c> optional-seam
/// posture, PLAN T119/gh-#224).</b> "Today" for the <c>{MM-dd}.json</c> filename and the request path
/// alike is the station's own calendar day — <see cref="IStationClockProvider.LocalNow"/>/
/// <see cref="IStationClockProvider.Zone"/> when the composition supplies one, else
/// <see cref="TimeProvider.LocalTimeZone"/> (the container's own clock, the pre-gh-#224 fallback every
/// other optional consumer of this seam already uses) — never UTC, which would flip which calendar day
/// is "today" for any station west of Greenwich in the evening. Both the request URI's month/day path
/// segments and the <c>{MM-dd}.json</c> filename are built directly from this resolved
/// <see cref="DateTime"/>'s own <see cref="DateTime.Month"/>/<see cref="DateTime.Day"/> — never from a
/// parsed or caller-supplied string — so there is no traversal surface to defend against at all: a real
/// calendar date can only ever render two zero-padded digits per segment.
/// </para>
///
/// <para>
/// <b>Cache-first, always (SPEC F109.2's three outcomes fall straight out of one rule).</b> Today's
/// day file is checked BEFORE any network call — a cache hit costs zero outbound requests, full stop,
/// whether or not Wikimedia happens to be reachable right now. That single rule produces all three
/// acceptance outcomes: a cache hit never touches the network (whether Wikimedia is up or down); a
/// cache MISS falls through to a live fetch, which — on success — is written back to the day file and
/// served; and a cache miss with an unreachable/malformed reply has nothing left to fall back to, so
/// this returns <see langword="null"/> (F107.6 skip-never-silence; <see cref="ContextPipeline"/> logs
/// the cause).
/// </para>
///
/// <para>
/// <b>Next-day pre-fetch (SPEC F109.2's "fallback context segment" duty).</b> After today's content is
/// resolved (from cache or a fresh fetch — either way), this class attempts to warm TOMORROW's day
/// file too, unless it already exists, so a Wikimedia outage that starts exactly at midnight still has
/// a ready file the moment "today" rolls over. Best-effort: a failed pre-fetch never fails the call
/// that already resolved TODAY's content, and is silently retried the next time this method runs.
/// </para>
///
/// <para>
/// <b>Sweep (mirrors <c>TtsSegmentSource.SweepBlurbs</c>'s own opportunistic, best-effort shape).</b>
/// Every <c>{MM-dd}.json</c> filename recurs exactly once a year, so a day file — once written — would
/// otherwise sit as a permanent cache hit and never be re-fetched at all. <see cref="RetentionHorizon"/>
/// (one week — comfortably longer than the two-day "today + tomorrow's pre-fetch" window that must
/// always survive a sweep, comfortably shorter than the 365-day span before a filename recurs) ages a
/// day file out after roughly a week of not being touched, so the SAME calendar date gets re-fetched
/// (and can pick up any curation edit Wikimedia has made since) the next time it comes around, rather
/// than serving a potentially year-stale file forever.
/// </para>
///
/// <para>
/// <b>Facts are single-line, plain text — sanitized by the pipeline, not here.</b> This class never
/// calls <see cref="ContextFactSanitizer"/> itself: <see cref="ContextContent.SegmentFacts"/>/
/// <see cref="ContextContent.PatterFact"/> carry the raw (Wikimedia-authored, hence untrusted) text
/// straight through — <see cref="ContextPipeline.EnsureFetchedAsync"/> is the ONE chokepoint every
/// provider's content passes through before being cached or vended (see
/// <see cref="ContextFactSanitizer"/>'s own remarks for why that call site, not this one).
/// </para>
///
/// <para>
/// <b>Wikimedia etiquette (SPEC F109.1, the F76 MusicBrainz precedent).</b> Every request carries a
/// descriptive, version-stamped <see cref="UserAgent"/> identifying GenWave and a contact URL — built
/// by the shared <see cref="GenWave.Core.Http.EtiquetteUserAgent"/> helper (F7 fix, T228 review — this
/// class's own construction used to be a hand-copied twin of <c>MusicBrainzYearLookup</c>'s; see that
/// helper's own remarks) from this assembly's build-stamped <c>AssemblyInformationalVersionAttribute</c>
/// (SPEC F65.1), never a hardcoded literal.
/// </para>
/// </summary>
public sealed class HistoryContextProvider(
    HttpClient http, IContextCacheRootProvider cacheRootProvider, TimeProvider timeProvider,
    ILogger<HistoryContextProvider> logger, IStationClockProvider? stationClock = null)
    : IContextProvider, ISelfGatingContextProvider
{
    /// <summary>The fixed, keyless Wikimedia host (SPEC F109.1) — set as this typed client's
    /// <see cref="HttpClient.BaseAddress"/> in <c>ContextServiceCollectionExtensions</c>, never
    /// overridable by config or a caller.</summary>
    public const string WikimediaBaseAddress = "https://api.wikimedia.org/";

    /// <summary>Response-buffer ceiling for this typed client (mirrors
    /// <c>MusicBrainzYearLookup.MaxResponseContentBytes</c>'s own rationale): the real
    /// <c>selected</c> reply curl'd at T228 build time was ~185 KB (20 entries, each carrying a
    /// <c>pages</c> section with thumbnails/extracts this class never reads) — comfortably under this
    /// bound with headroom for a busier day, while still capping what a misbehaving/compromised
    /// endpoint could make this client buffer.</summary>
    public const long MaxResponseContentBytes = 1_048_576;

    /// <summary>The project's public repository — the "contact URL" half of the etiquette
    /// User-Agent (mirrors <c>MusicBrainzYearLookup.ProjectUrl</c>).</summary>
    const string ProjectUrl = "https://github.com/GenWave-Org/genwave";

    /// <summary>"GenWave/&lt;version&gt; (+repo)" (SPEC F109.1, the F76 MusicBrainz precedent),
    /// byte-identical to the pre-F7 construction — see
    /// <see cref="GenWave.Core.Http.EtiquetteUserAgent"/>'s own remarks for how the version segment is
    /// derived and why this assembly's own <see cref="System.Reflection.Assembly"/> is passed
    /// explicitly rather than resolved inside the shared helper.</summary>
    static readonly string UserAgent = EtiquetteUserAgent.Build(typeof(HistoryContextProvider).Assembly, ProjectUrl);

    /// <summary>How long a day file survives with no cache hit at all before the next sweep removes it
    /// (see this class's own remarks for why last-write time, not a "fetched at" stamp inside the
    /// file, is the right signal here). Deliberately well above the 2-day "today + tomorrow's
    /// pre-fetch" window that must always survive, well below the 365-day span before a filename
    /// recurs.</summary>
    static readonly TimeSpan RetentionHorizon = TimeSpan.FromDays(7);

    /// <summary>SPEC F109.1's stated segment shape: 2-4 curated entries. A day's entry list with fewer
    /// than this many (down to a legal single entry — only a truly EMPTY list is "nothing to say", per
    /// F109.1) is used in full; a longer list (Wikimedia's real <c>selected</c> reply carries ~20) is
    /// trimmed to this many, in the order Wikimedia returned them.</summary>
    const int MaxSegmentEntries = 4;

    /// <summary>Joins multiple facts into one <see cref="ContextContent.SegmentFacts"/> string — never
    /// a newline (SPEC F109.2's own explicit requirement; also enforced structurally by the pipeline's
    /// own <see cref="ContextFactSanitizer"/>, this is belt-and-suspenders at the source).</summary>
    const string FactSeparator = " · ";

    /// <summary>SPEC F109.1's stated shipped defaults for this provider — the C# home
    /// <c>appsettings.json</c>'s <c>Context:History:*</c> seed literals pin against
    /// (STORY-151's <c>ScenarioSeedsEqualTheInitializers</c>, F3 fix, T226 review): off by default
    /// (fail-closed), a fresh segment/patter fact may surface once every four hours — well above
    /// SPEC F108.2's weather-only 30-minute floor, and well inside a single Wikimedia On-This-Day
    /// day file's own multi-hour relevance — no patter cadence. Before this fix, 240 existed ONLY as
    /// an appsettings.json literal with no C# home a test could pin against;
    /// <c>GenWave.Host.Options.ConfigurationContextSettingsProvider</c>'s own generic 60-minute
    /// fallback (deliberately provider-agnostic, see its own remarks) is not this number — the seed
    /// is what actually reaches a fresh deploy.</summary>
    public const bool DefaultEnabled = false;

    /// <summary>See <see cref="DefaultEnabled"/>'s own remarks.</summary>
    public const int DefaultSegmentCadenceMinutes = 240;

    /// <summary>See <see cref="DefaultEnabled"/>'s own remarks.</summary>
    public const int DefaultPatterCadenceMinutes = 0;

    public string Key => "history";

    /// <summary>
    /// The <see cref="ISelfGatingContextProvider"/> hook <see cref="ContextPipeline"/> actually calls
    /// in production — re-runs the same cache-root check <see cref="FetchAsync"/> does below, cheaply
    /// and synchronously, with no I/O involved. Explicit interface implementation: this is a
    /// pipeline-facing seam, not part of this class's own public surface.
    /// </summary>
    bool ISelfGatingContextProvider.IsAvailable => !string.IsNullOrWhiteSpace(cacheRootProvider.Root);

    public async Task<ContextContent?> FetchAsync(CancellationToken ct)
    {
        // Read fresh — never cache IContextCacheRootProvider.Root on this instance (its own
        // contract) — mirrors WeatherContextProvider's own coordinate re-read.
        var cacheRoot = cacheRootProvider.Root;
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            // The fail-closed config line: in production ContextPipeline checks
            // ISelfGatingContextProvider.IsAvailable BEFORE ever reaching this line (see that
            // interface's own remarks), so this call site is the defense-in-depth backstop only.
            logger.LogInformation("Context provider {ProviderKey} is off: no cache root is wired.", Key);
            return null;
        }

        var zone = stationClock?.Zone ?? timeProvider.LocalTimeZone;
        var localNow = stationClock?.LocalNow ?? TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        var today = localNow.Date;

        var cacheDir = Path.Combine(cacheRoot, "context", "history");
        var entries = await LoadOrFetchDayAsync(cacheDir, today, ct).ConfigureAwait(false);

        if (entries is null)
            return null; // Neither a cache hit nor a live fetch — F107.6 skip; the pipeline logs the cause.

        // Best-effort resilience/hygiene (see this class's own remarks) — neither can fail TODAY's
        // already-resolved content, which is returned below regardless of either outcome.
        var tomorrow = today.AddDays(1);
        await PreFetchTomorrowAsync(cacheDir, tomorrow, ct).ConfigureAwait(false);
        SweepOldDayFiles(cacheDir);

        // FreshUntil = end of the station-local day (SPEC F109.2): the facts are date-anchored, so
        // they stay servable for exactly as long as "today" (the day they were fetched/cached for)
        // still is today, in the station's own zone.
        var freshUntil = new DateTimeOffset(tomorrow, zone.GetUtcOffset(tomorrow));

        return BuildContent(entries, freshUntil);
    }

    /// <summary>Cache-first (see this class's own remarks): a valid day file is used with zero
    /// network; otherwise a live fetch runs and, on success, is written back for next time.</summary>
    async Task<IReadOnlyList<HistoryDayCacheEntry>?> LoadOrFetchDayAsync(string cacheDir, DateTime date, CancellationToken ct)
    {
        var path = DayFilePath(cacheDir, date);

        if (await TryReadCacheAsync(path, ct).ConfigureAwait(false) is { } cached)
            return cached;

        var fetched = await TryFetchFromWikimediaAsync(date, ct).ConfigureAwait(false);
        if (fetched is null)
            return null;

        await WriteCacheAsync(cacheDir, path, fetched, ct).ConfigureAwait(false);
        return fetched;
    }

    /// <summary>Warms tomorrow's day file when it is not already cached — best-effort, silent on
    /// failure (see this class's own remarks: retried the next time this method runs).</summary>
    async Task PreFetchTomorrowAsync(string cacheDir, DateTime tomorrow, CancellationToken ct)
    {
        var path = DayFilePath(cacheDir, tomorrow);
        if (File.Exists(path))
            return;

        var fetched = await TryFetchFromWikimediaAsync(tomorrow, ct).ConfigureAwait(false);
        if (fetched is not null)
            await WriteCacheAsync(cacheDir, path, fetched, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads and parses <paramref name="path"/>. A missing file is an ordinary cache miss
    /// (<see langword="null"/>, no log line — this is expected the first time any given date is ever
    /// asked for). A file that exists but fails to parse (truncated write, hand-edited, disk
    /// corruption) is DEFENSIVE — logged once and deleted so the next write starts clean — never a
    /// thrown exception past this method (SPEC F109's "corrupt file ⇒ treat as absent + delete").
    /// </summary>
    async Task<IReadOnlyList<HistoryDayCacheEntry>?> TryReadCacheAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            var cache = await JsonSerializer.DeserializeAsync<HistoryDayCache>(stream, cancellationToken: ct).ConfigureAwait(false);

            if (cache?.Entries is not { Count: > 0 } entries)
            {
                DeleteCacheFileBestEffort(path);
                return null;
            }

            return entries;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancellation (e.g. shutdown) — not a corrupt-file fault.
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Context provider {ProviderKey} found a corrupt day-cache file {Path}; discarding it", Key, path);
            DeleteCacheFileBestEffort(path);
            return null;
        }
    }

    static void DeleteCacheFileBestEffort(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best-effort cleanup — a delete failure (locked file, permission denied, a race with a
            // concurrent delete) must never escalate; TryReadCacheAsync already treats this file as
            // absent regardless, and a future write will overwrite it if it is ever readable again.
        }
    }

    /// <summary>Live Wikimedia call for <paramref name="date"/>'s On-This-Day <c>selected</c> feed.
    /// Every failure — HTTP error, timeout, malformed/empty reply — collapses to
    /// <see langword="null"/>, silently (mirrors <c>WeatherContextProvider.FetchAsync</c>'s own
    /// restraint: this is F107.6's ordinary skip-never-silence outcome, and the pipeline already owns
    /// the once-per-slot Information line for whichever caller ultimately returns null).</summary>
    async Task<IReadOnlyList<HistoryDayCacheEntry>?> TryFetchFromWikimediaAsync(DateTime date, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(date));
            // Set per-request, not on the shared HttpClient (mirrors MusicBrainzYearLookup — keeps
            // this seam testable against a captured HttpRequestMessage).
            request.Headers.UserAgent.ParseAdd(UserAgent);

            var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode(); // throws HttpRequestException on non-2xx

            var payload = await response.Content
                .ReadFromJsonAsync<WikimediaOnThisDayResponse>(cancellationToken: ct).ConfigureAwait(false);

            if (payload?.Selected is not { Count: > 0 } selected)
                return null; // Absent or empty selected list (F109.1) — a legal "nothing today" outcome.

            // Unknown/absent fields ⇒ that ONE entry is skipped, not the whole day (F109.1). A foreach
            // with a pattern-matched local (not a Where().Select() chain) so the compiler can actually
            // track that Year/Text are non-null within this scope — no null-forgiving operator needed.
            var entries = new List<HistoryDayCacheEntry>();
            foreach (var candidate in selected)
            {
                if (candidate.Year is { } year && !string.IsNullOrWhiteSpace(candidate.Text))
                    entries.Add(new HistoryDayCacheEntry(year, candidate.Text.Trim()));
            }

            return entries.Count > 0 ? entries : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancellation — not a Wikimedia fault.
        }
        catch (Exception)
        {
            return null; // Outage/malformed reply — F107.6 skip-never-silence.
        }
    }

    /// <summary>Persists <paramref name="entries"/> to <paramref name="path"/> via a temp-file +
    /// atomic move (never a partially-written file a later read could trip over). Best-effort: a
    /// write failure never fails the caller, which already has <paramref name="entries"/> in hand
    /// regardless of whether this succeeds — the next fetch attempt simply retries the write.</summary>
    async Task WriteCacheAsync(string cacheDir, string path, IReadOnlyList<HistoryDayCacheEntry> entries, CancellationToken ct)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(cacheDir);

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, new HistoryDayCache(entries), cancellationToken: ct)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // F6 fix, T228 review: cancellation must not leak the temp file either — a rethrow with no
            // cleanup left an orphaned "{path}.{guid}.tmp" behind on every shutdown that raced a write.
            DeleteCacheFileBestEffort(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Context provider {ProviderKey} failed to persist day-cache file {Path}", Key, path);
            DeleteCacheFileBestEffort(tempPath);
        }
    }

    /// <summary>Deletes day-cache files whose last-write time is older than
    /// <see cref="RetentionHorizon"/> — mirrors <c>TtsSegmentSource.SweepBlurbs</c>'s own opportunistic
    /// posture exactly (best-effort, stops at the first failure, retried on the next call). See this
    /// class's own remarks for why a filename-recurs-yearly cache needs this at all.
    ///
    /// <para>
    /// Sweeps <c>*.tmp</c> alongside <c>*.json</c> (F6 fix, T228 review): <see cref="WriteCacheAsync"/>'s
    /// own temp file is named <c>"{path}.{guid}.tmp"</c>, which the old <c>*.json</c>-only glob never
    /// matched — an orphan left behind by a crash mid-write (before either of that method's own
    /// best-effort deletes could run) would otherwise sit forever, never reclaimed. Same retention
    /// window as a day file: an orphan is "stale" by the same clock, not a special-cased faster one.
    /// </para>
    /// </summary>
    void SweepOldDayFiles(string cacheDir)
    {
        try
        {
            if (!Directory.Exists(cacheDir))
                return;

            var cutoff = timeProvider.GetUtcNow().UtcDateTime - RetentionHorizon;
            var candidates = Directory.EnumerateFiles(cacheDir, "*.json").Concat(Directory.EnumerateFiles(cacheDir, "*.tmp"));
            foreach (var entry in candidates)
            {
                if (File.GetLastWriteTimeUtc(entry) < cutoff)
                    File.Delete(entry);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Context provider {ProviderKey} day-cache sweep failed for {CacheDir}", Key, cacheDir);
        }
    }

    static string DayFilePath(string cacheDir, DateTime date) =>
        Path.Combine(cacheDir, $"{date.ToString("MM-dd", CultureInfo.InvariantCulture)}.json");

    /// <summary>Both path segments are built directly from <paramref name="date"/>'s own
    /// <c>Month</c>/<c>Day</c> — never from a parsed or caller-supplied string (see this class's own
    /// remarks: no traversal surface exists to defend against here).</summary>
    static string BuildRequestUri(DateTime date) =>
        "feed/v1/wikipedia/en/onthisday/selected/" +
        $"{date.ToString("MM", CultureInfo.InvariantCulture)}/{date.ToString("dd", CultureInfo.InvariantCulture)}";

    static ContextContent BuildContent(IReadOnlyList<HistoryDayCacheEntry> entries, DateTimeOffset freshUntil)
    {
        var chosen = entries.Take(MaxSegmentEntries).ToList();
        var segmentFacts = string.Join(FactSeparator, chosen.Select(FormatEntry));
        // One entry, compact (SPEC F109.1) — the first curated entry Wikimedia returned, same
        // formatting as a segment fact.
        var patterFact = FormatEntry(chosen[0]);

        return new ContextContent(segmentFacts, patterFact, freshUntil);
    }

    static string FormatEntry(HistoryDayCacheEntry entry) =>
        $"{entry.Year.ToString(CultureInfo.InvariantCulture)}: {entry.Text}";
}
