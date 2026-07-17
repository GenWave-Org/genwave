namespace GenWave.MediaLibrary.YearLookup;

using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.MediaLibrary.Options;

/// <summary>
/// MusicBrainz-backed <see cref="IYearLookup"/> (SPEC F48.1-F48.2, closes gitea-#208; comparator revised
/// SPEC F60, closes gitea-#228): a recording search against <c>Library:YearLookup:Endpoint</c>. A
/// candidate is accepted iff its score is ≥ <c>Library:YearLookup:MinScore</c> AND at least one of
/// its artist-credit names matches the queried artist after normalization (trim + case-insensitive).
/// Among ALL qualifying candidates, the one selected is whichever has the OLDEST earliest-release
/// year — the earliest year among its releases' dates, falling back to the recording's own
/// <c>first-release-date</c> when no release carries a parseable date — NOT the highest-scoring one
/// (F60.1): a later re-recording/live take can outscore the original on MusicBrainz's own match
/// confidence, but any qualifying candidate is "the same song", and decade-mix semantics want the
/// original release.
///
/// No boot-frozen endpoint (the F36.2 precedent): <c>Library:YearLookup:Endpoint</c> is read from
/// <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> fresh on every call. The per-request timeout
/// is a <see cref="CancellationTokenSource"/> linked to the caller's token (NOT
/// <see cref="HttpClient.Timeout"/>) — testable and scoped to this one call, mirroring
/// <c>LlmCopyWriter.RequestCompletionAsync</c>.
///
/// Never throws past this boundary (F48.1-F48.2): any HTTP error, non-2xx status, malformed JSON, or
/// the internal timeout firing all collapse to <see langword="null"/> — the legal "no confident match"
/// outcome. Deliberately silent on the way there (no logger dependency), mirroring
/// <c>AubioBpmAnalyzer</c>/<c>FfmpegCueAnalyzer</c>'s restraint — the backfill pipeline (F48.3, F48.5)
/// is what aggregates failures into a once-per-tick WARN, not this per-call seam.
///
/// Also implements <see cref="IYearLookupDiagnostics"/> (X5, F48.5): <see cref="LastCallFailed"/>
/// distinguishes an endpoint-level failure (timeout, connect failure, non-2xx, malformed body) from
/// a legal "no confident match" — a successful round trip clears it regardless of whether a
/// candidate qualified. This is purely additive to the class; the <see cref="IYearLookup"/> contract
/// itself is unchanged.
/// </summary>
public sealed class MusicBrainzYearLookup(HttpClient http, IOptionsMonitor<YearLookupOptions> optionsMonitor)
    : IYearLookup, IYearLookupDiagnostics
{
    const string UserAgent = "GenWave/1.0 (+https://github.com/GenWave-Org/genwave)";

    /// <summary>
    /// Response-buffer ceiling for this typed client (review finding — mirrors
    /// <c>LlmCopyWriter.MaxResponseContentBytes</c>'s own bound and rationale): a recording-search
    /// reply capped at <c>limit=5</c> is a few KB of JSON, never megabytes — a
    /// misbehaving/compromised endpoint shouldn't be able to make this client buffer an unbounded
    /// response body. Applied to the <see cref="HttpClient"/> in
    /// <c>MediaLibraryServiceCollectionExtensions</c> via <c>HttpClient.MaxResponseContentBufferSize</c>.
    /// </summary>
    public const long MaxResponseContentBytes = 1_048_576;

    /// <summary>
    /// See <see cref="IYearLookupDiagnostics"/>. Not thread-safe against concurrent
    /// <see cref="TryLookupAsync"/> calls — safe under the production caller (SPEC F48.3's "one
    /// request in flight" pacing), which never overlaps two calls to this instance.
    /// </summary>
    public bool LastCallFailed { get; private set; }

    public async Task<int?> TryLookupAsync(string artist, string title, string? album, CancellationToken ct)
    {
        try
        {
            var cfg = optionsMonitor.CurrentValue;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(cfg.Endpoint, artist, title, album));
            // Set per-request, not on the shared HttpClient (F48.1) — keeps this seam testable
            // against a captured HttpRequestMessage rather than a client-wide default header.
            request.Headers.UserAgent.ParseAdd(UserAgent);

            var response = await http.SendAsync(request, timeoutCts.Token);
            response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

            var payload = await response.Content.ReadFromJsonAsync<MusicBrainzRecordingSearchResponse>(timeoutCts.Token);

            // A response was successfully received and parsed — this is a completed round trip,
            // regardless of whether a candidate ends up qualifying (F48.5's failure/no-match split).
            LastCallFailed = false;
            return SelectYear(payload, artist, cfg.MinScore);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller cancelled (e.g. shutdown) — not our own TimeoutSeconds budget expiring, and
            // not an endpoint failure either. Propagate; this is not a "no confident match" outcome
            // to swallow, and LastCallFailed is deliberately left as-is (no signal to report).
            throw;
        }
        catch (Exception)
        {
            // Everything else lands here: our own timeout CTS firing, a non-2xx status
            // (EnsureSuccessStatusCode), a connect failure, malformed JSON. Every one of these is
            // the legal "no confident match" outcome for this seam (F48.1-F48.2) — never an
            // exception past the boundary — but IS an endpoint-level failure for F48.5's diagnostic.
            LastCallFailed = true;
            return null;
        }
    }

    static Uri BuildRequestUri(string endpoint, string artist, string title, string? album)
    {
        var query = BuildLuceneQuery(artist, title, album);
        var baseUri = $"{endpoint.TrimEnd('/')}/recording";

        // QueryHelpers.AddQueryString does the percent-encoding — no hand-rolled escaping of the
        // query STRING itself (the lucene value escaping below is a separate, syntactic concern).
        return new Uri(QueryHelpers.AddQueryString(baseUri, new Dictionary<string, string?>
        {
            ["query"] = query,
            ["fmt"] = "json",
            ["limit"] = "5",
        }));
    }

    /// <summary>
    /// Builds the lucene recording-search query: <c>artist:"..." AND recording:"..."</c>, plus
    /// <c>AND release:"..."</c> when <paramref name="album"/> is present. Embedded quotes/backslashes
    /// in any tag value are backslash-escaped so a tag like <c>He said "hi"</c> cannot break the query
    /// syntax — URL encoding of the resulting string happens separately in <see cref="BuildRequestUri"/>.
    /// </summary>
    static string BuildLuceneQuery(string artist, string title, string? album)
    {
        var query = $"artist:\"{EscapeLuceneValue(artist)}\" AND recording:\"{EscapeLuceneValue(title)}\"";
        if (!string.IsNullOrWhiteSpace(album))
            query += $" AND release:\"{EscapeLuceneValue(album)}\"";

        return query;
    }

    static string EscapeLuceneValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// Among ALL candidates passing the F48.2 confidence rule (score ≥ <paramref name="minScore"/>
    /// AND a normalized artist match), returns the OLDEST earliest-release year — replacing
    /// best-score-first (SPEC F60.1, closes gitea-#228: a later, higher-scoring re-recording must never
    /// beat a qualifying original on year). A qualifying candidate whose year cannot be resolved (no
    /// parseable release date anywhere) does not participate in the comparison — it has nothing to
    /// offer "oldest" — so it is skipped rather than blocking a later, resolvable candidate the way a
    /// yearless highest-score pick used to. Ties on the oldest year are inherently value-identical
    /// (same year either way), so no tie-break policy is needed. Returns <see langword="null"/> when
    /// no candidate qualifies, or none of the qualifying candidates has a resolvable year (F48.2).
    /// </summary>
    static int? SelectYear(MusicBrainzRecordingSearchResponse? payload, string artist, int minScore)
    {
        if (payload?.Recordings is not { Count: > 0 } recordings)
            return null;

        var normalizedArtist = artist.Trim();

        int? oldestYear = null;

        foreach (var candidate in recordings)
        {
            if (candidate.Score < minScore)
                continue;
            if (!MatchesArtist(candidate, normalizedArtist))
                continue;

            var year = EarliestReleaseYear(candidate);
            if (year is null)
                continue;

            if (oldestYear is null || year < oldestYear)
                oldestYear = year;
        }

        return oldestYear;
    }

    static bool MatchesArtist(MusicBrainzRecording candidate, string normalizedArtist)
    {
        if (candidate.ArtistCredit is not { Count: > 0 } credits)
            return false;

        foreach (var credit in credits)
        {
            if (NameMatches(credit.Name, normalizedArtist) || NameMatches(credit.Artist?.Name, normalizedArtist))
                return true;
        }

        return false;
    }

    static bool NameMatches(string? candidateName, string normalizedArtist) =>
        candidateName is not null
            && string.Equals(candidateName.Trim(), normalizedArtist, StringComparison.InvariantCultureIgnoreCase);

    static int? EarliestReleaseYear(MusicBrainzRecording recording)
    {
        int? earliest = null;
        if (recording.Releases is { Count: > 0 } releases)
        {
            foreach (var release in releases)
            {
                if (ParseYear(release.Date) is int year && (earliest is null || year < earliest))
                    earliest = year;
            }
        }

        return earliest ?? ParseYear(recording.FirstReleaseDate);
    }

    /// <summary>Parses the leading 4-digit year out of a MusicBrainz partial date ("YYYY", "YYYY-MM", "YYYY-MM-DD").</summary>
    static int? ParseYear(string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || date.Length < 4)
            return null;

        return int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
    }
}
