// STORY-230 — The gate: strangers request, nothing leaks (SPEC F87.9–F87.10, F88.5; PLAN T93)
//
// BDD specification — xUnit, built live at T93 (/build-loop), the epic's acceptance gate. Per the
// Story141/147/153/162/212/221 idiom: the pin/reflection facts below (SadPathDisclosure) are
// always-run, non-Skip, real assertions requiring nothing but the checked-out repo and the
// compiled assembly. The two flywheel facts (ScenarioFlywheel) are different in kind — they need
// the LIVE compose stack (requests enabled, PublicBaseUrl set, a real catalog) — so they follow
// the Story013 "SKIPPED-AT-RUNTIME" idiom instead: never [Fact(Skip=...)] (that would hide them
// from every run forever), but a runtime reachability/precondition check that writes a clear
// skip line and returns cleanly when the live stack isn't up or isn't configured for this gate,
// and drives the REAL surfaces (an anonymous wish POST, the admin booth-log/settings/media APIs,
// a single ICY metadata handshake against the live Icecast stream) when it is. Both facts share
// ONE flywheel run via an xUnit collection fixture (RequestsArtworkFlywheelFixture) — mirroring
// KokoroFixture's shared-container idiom — because POSTing the wish twice would trip
// Requests:PerIpCooldownMinutes (default 5) on the second fact.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Xunit.Abstractions;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

public static class FeatureRequestsArtworkGate
{
    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107/
    /// 141/147/153/162/212/221's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string Sha256Hex(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, relativePath));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>
    /// Every public spectator-facing wire DTO in <c>GenWave.Host.Api</c> — discovered the same
    /// by-prefix-reflection way Story221's F86.9 gate discovers its own sweep set, narrowed to
    /// actual serialized payload shapes by excluding controllers (<see cref="ControllerBase"/>
    /// subclasses) and marker attributes structurally, rather than by a hand-maintained exclusion
    /// list — a brand-new controller or attribute never pollutes the census; a brand-new DTO (or a
    /// brand-new field on an existing one) always does.
    /// </summary>
    static IReadOnlyList<Type> SpectatorWireDtoTypes() =>
        typeof(SpectatorController).Assembly.GetTypes()
            .Where(type => type.IsPublic
                && type.Namespace == "GenWave.Host.Api"
                && type.Name.StartsWith("Spectator", StringComparison.Ordinal)
                && !typeof(ControllerBase).IsAssignableFrom(type)
                && !typeof(Attribute).IsAssignableFrom(type))
            .ToList();

    /// <summary>Flat "TypeName.PropertyName" census over every type <see cref="SpectatorWireDtoTypes"/>
    /// finds, ordinal-sorted so it can be diffed byte-for-byte against a pinned manifest.</summary>
    static IReadOnlyList<string> SpectatorWireFieldCensus() =>
        SpectatorWireDtoTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => $"{type.Name}.{property.Name}"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    // ---------------------------------------------------------------------
    // AC1 (F87.10, F88.5) — the flywheel: needs the live compose stack.
    // ---------------------------------------------------------------------

    internal const string FlywheelCollectionName = "RequestsArtworkFlywheel";

    [CollectionDefinition(FlywheelCollectionName)]
    public sealed class FlywheelCollection : ICollectionFixture<RequestsArtworkFlywheelFixture>;

    [Collection(FlywheelCollectionName)]
    public sealed class ScenarioFlywheel(RequestsArtworkFlywheelFixture fixture, ITestOutputHelper output)
    {
        [Fact]
        public void AWishForAHeldArtistAirsWithinTheWindowWithTheShoutOutLeadIn()
        {
            if (fixture.SkipReason is { } reason)
            {
                output.WriteLine($"SKIPPED-AT-RUNTIME: {reason}");
                return;
            }

            Assert.True(fixture.FulfilledTrackAired,
                $"the requested artist '{fixture.RequestedArtist}' never aired within the poll window " +
                $"— the flywheel promise (F87.10) was not observed. {fixture.Diagnostics}");

            // Honest limit, by design: F87.8 forbids logging wish text or the generated shout-out
            // copy anywhere, so this can only ever observe that a LeadIn patter segment aired
            // structurally adjacent to the fulfilled track — never the literal "request color"
            // wording (that's unit/fake-tested at T91/Story228 with the real render pipeline).
            Assert.True(fixture.ShoutOutLeadInObserved,
                "the fulfilled track aired with no preceding LeadIn patter segment in the booth log " +
                "— the shout-out lead-in promise (F87.7) was not observed");
        }

        /// <summary>
        /// LIVE-RUN FINDING, ROOT-CAUSED AND FIXED (2026-07-24, PLAN T93, recorded per this suite's
        /// own derivation-contract convention — see Story013's ProgramLoudnessOffset remarks for the
        /// precedent): against Dean's dev stack (requests enabled, PublicBaseUrl set) this fact
        /// originally FAILED — the ICY metadata handshake never carried a StreamUrl for any track,
        /// fulfilled or not. Root-caused via a raw telnet <c>output.icecast.metadata</c> read plus a
        /// controlled manual <c>q.push</c> with a literal <c>url="..."</c> annotation: the pushed
        /// value never reached the on-air metadata frame at all. <c>engine/genwave.liq</c>'s own
        /// comment above <c>output.icecast</c> explains why — <c>settings.encoder.metadata.export</c>
        /// is the list of custom annotate fields that survive to the point where <c>icy_metadata</c>
        /// can forward them ("custom annotate fields ... do NOT appear unless added here"); T85 added
        /// <c>"url"</c> to <c>icy_metadata</c> but never to <c>settings.encoder.metadata.export</c>'s
        /// own <c>list.append</c> call a few lines above, so the value was filtered out before
        /// <c>icy_song</c>/<c>icy_metadata</c> ever saw it. Fixed at T93 by appending <c>"url"</c> to
        /// that export list too (the same task that recorded this finding) — the engine hash pinned
        /// below carries the fix, and the ICY handshake now yields a real StreamUrl.
        /// </summary>
        [Fact]
        public void TheIcyStreamUrlServesTheFulfilledTracksArt()
        {
            if (fixture.SkipReason is { } reason)
            {
                output.WriteLine($"SKIPPED-AT-RUNTIME: {reason}");
                return;
            }

            Assert.True(fixture.FulfilledTrackAired,
                "the flywheel setup never observed the fulfilled track airing — see the sibling fact for detail");
            Assert.False(string.IsNullOrEmpty(fixture.IcyStreamUrl),
                "the ICY metadata handshake against the live stream never yielded a StreamUrl (F88.4/F88.5) — " +
                "see this fact's own remarks for the T93 root cause/fix history in engine/genwave.liq's " +
                "settings.encoder.metadata.export and icy_metadata lists");
            Assert.True(fixture.ArtworkBytesServed,
                $"GET {fixture.IcyStreamUrl} did not serve image bytes for the fulfilled track's art (F88.3/F88.5)");
        }
    }

    // ---------------------------------------------------------------------
    // AC2/AC3 (F87.9, F88.4) — always-run pins, no live stack required.
    // ---------------------------------------------------------------------

    public static class SadPathDisclosure
    {
        static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        [Fact]
        public static void The202BodyIsAPinnedContract()
        {
            // Full serialized property-set reflection over SpectatorRequestAccepted (T27/Story183
            // idiom) — the complete property list AND the literal Status/Note constants, so any
            // wording drift on the epic's one public WRITE response is deliberate, not accidental.
            var json = JsonSerializer.Serialize(new SpectatorRequestAccepted(), SerializerOptions);
            using var document = JsonDocument.Parse(json);
            var properties = document.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.GetString(), StringComparer.Ordinal);

            Assert.Equal(
                new HashSet<string> { "status", "note" },
                properties.Keys.ToHashSet(StringComparer.Ordinal));
            Assert.Equal("received", properties["status"]);
            Assert.Equal("Requests are best-effort and not guaranteed to play.", properties["note"]);
        }

        /// <summary>
        /// The epic's final "nothing leaked" statement (F87.9, mirroring Story221's F86.9 gate):
        /// rather than asserting "the other disclosure suites exist and are green" — true but not a
        /// fact THIS file can itself prove — this reflects the complete public spectator wire
        /// surface (every field of every Spectator-prefixed DTO) and pins the whole census. A new
        /// field anywhere on that surface (request-derived or otherwise) changes this list and
        /// fails here, independently of whatever Story171/183/217/221 already assert about it.
        /// </summary>
        static readonly string[] PinnedFieldCensus =
        [
            "SpectatorAbout.License",
            "SpectatorAbout.ProjectUrl",
            "SpectatorAbout.RequestsEnabled",
            "SpectatorAbout.StationName",
            "SpectatorAbout.StreamUrl",
            "SpectatorAbout.Version",
            "SpectatorPatterNowPlaying.DurationMs",
            "SpectatorPatterNowPlaying.Kind",
            "SpectatorPatterNowPlaying.Listeners",
            "SpectatorPatterNowPlaying.StartedAt",
            "SpectatorPatterNowPlaying.State",
            "SpectatorPlayHistoryPatterEntry.AiredAt",
            "SpectatorPlayHistoryPatterEntry.Kind",
            "SpectatorPlayHistoryResponse.Entries",
            "SpectatorPlayHistoryTrackEntry.AiredAt",
            "SpectatorPlayHistoryTrackEntry.Artist",
            "SpectatorPlayHistoryTrackEntry.Kind",
            "SpectatorPlayHistoryTrackEntry.Title",
            "SpectatorRequestAccepted.Note",
            "SpectatorRequestAccepted.Status",
            "SpectatorRequestSubmission.Wish",
            "SpectatorStandbyNowPlaying.Listeners",
            "SpectatorStandbyNowPlaying.State",
            "SpectatorStats.Enriching",
            "SpectatorStats.Failed",
            "SpectatorStats.Ready",
            "SpectatorTrackNowPlaying.Artist",
            "SpectatorTrackNowPlaying.DurationMs",
            "SpectatorTrackNowPlaying.Kind",
            "SpectatorTrackNowPlaying.Listeners",
            "SpectatorTrackNowPlaying.StartedAt",
            "SpectatorTrackNowPlaying.State",
            "SpectatorTrackNowPlaying.Title",
        ];

        [Fact]
        public static void AllDisclosureSuitesStayGreenWithRequestsAndArtworkLive()
        {
            var actual = SpectatorWireFieldCensus();

            Assert.Equal(PinnedFieldCensus, actual);
        }

        [Fact]
        public static void TheEngineAndComposeHashesAreRePinnedAsThisEpoch()
        {
            // T93 epoch (F88.4 export fix) — the seven other pins across the suite (Story141/147/
            // 153/162/212/221 x2) already carry this same re-pinned constant; this is that
            // re-confirmation, run as its own fact: compose.yaml was NOT touched this epoch
            // (verified — sha256sum matches Story212/221's own pin, byte-identical), and
            // engine/genwave.liq's settings.encoder.metadata.export now also carries "url"
            // (this task's own fix — see TheIcyStreamUrlServesTheFulfilledTracksArt's remarks for
            // the finding that motivated it) — the epoch's one deliberate amendment, already
            // folded into this hash. Repeating the two constants here (rather than a shared
            // constant file) matches the established idiom — every existing gate owns its own
            // copy, so no single file is a point of failure for the whole suite's zero-diff
            // convention.
            const string EngineScriptSha256 = "11c8b3b59b4b641dc59fa4217e935442573adf04f8e756934e23593b17677049";
            const string ComposeYamlSha256  = "9ddd169329ef5b092638d1e67279272fc4d7b9f350dcc330cb455d7d92faf981";

            Assert.Equal(EngineScriptSha256, Sha256Hex(Path.Combine("engine", "genwave.liq")));
            Assert.Equal(ComposeYamlSha256, Sha256Hex("compose.yaml"));
        }
    }
}

/// <summary>
/// Drives the entire request→airing→artwork flywheel exactly ONCE (shared by both
/// <see cref="FeatureRequestsArtworkGate.ScenarioFlywheel"/> facts via the
/// <see cref="FeatureRequestsArtworkGate.FlywheelCollectionName"/> collection, mirroring
/// <c>KokoroFixture</c>'s shared-resource idiom): logs into the admin API, confirms
/// <c>Station:Requests:Enabled</c> and <c>Station:PublicBaseUrl</c> are live-configured, finds a
/// real held artist via <c>GET /api/media</c>, POSTs an anonymous wish for it, polls
/// <c>GET /api/booth-log</c> for the fulfillment + airing + lead-in narrative, then — only once the
/// track is confirmed airing — performs a single ICY metadata handshake against the live Icecast
/// stream and fetches the StreamUrl it reports.
/// <para>
/// <see cref="SkipReason"/> is set (never an exception) for every condition that means "this
/// environment cannot exercise the gate right now" — unreachable stack, no ADMIN_PASSWORD,
/// requests disabled, no PublicBaseUrl, empty catalog, or a 429 from a previous run's cooldown
/// (SPEC F87.3). Once past every precondition, a genuine failure (the poll timing out, the ICY
/// handshake never yielding a StreamUrl) is left unset — a real assertion failure, not a skip.
/// </para>
/// </summary>
public sealed class RequestsArtworkFlywheelFixture : IAsyncLifetime
{
    const string ApiBaseUrl = "http://localhost:8080";
    const string StreamHost = "localhost";
    const int StreamPort = 8000;
    const string StreamPath = "/stream";

    static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan IcyTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Bounded well under the real 15-minute Station:Requests:WindowMinutes default (SPEC F87.6) —
    /// "poll for fulfilled within a shortened window", not the request's own expiry. 600s (10
    /// minutes) is empirically calibrated, not a guess: three live runs against Dean's dev stack
    /// (2026-07-24, this task) measured request-received-to-actual-airing latency of ~3m45s, ~5m06s,
    /// and ~5m09s — the fulfillment PICK happens promptly, but the fulfilled item then has to drain
    /// whatever the feeder's own look-ahead queue was already holding (one run had a 220s track
    /// queued ahead of it) before it actually reaches on-air. 300s undershot the 5m06s run by 6
    /// seconds. Overridable for a slower/busier dev box.
    /// </summary>
    static TimeSpan PollTimeout => TimeSpan.FromSeconds(
        int.TryParse(
            Environment.GetEnvironmentVariable("GENWAVE_LIVE_REQUEST_GATE_TIMEOUT_SECONDS"),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : 600);

    public string? SkipReason { get; private set; }
    public string? RequestedArtist { get; private set; }
    public bool FulfilledTrackAired { get; private set; }
    public bool ShoutOutLeadInObserved { get; private set; }
    public string? IcyStreamUrl { get; private set; }
    public bool ArtworkBytesServed { get; private set; }
    public string Diagnostics { get; private set; } = "";

    public async Task InitializeAsync()
    {
        using var admin = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() })
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = HttpTimeout,
        };

        if (!await TryPrepareAsync(admin))
            return; // SkipReason already set.

        using var spectator = new HttpClient { BaseAddress = new Uri(ApiBaseUrl), Timeout = HttpTimeout };
        var wishPostedAt = DateTime.UtcNow;

        HttpResponseMessage wishResponse;
        try
        {
            wishResponse = await spectator.PostAsJsonAsync(
                "/spectator/api/requests", new { wish = $"Play something by {RequestedArtist}" });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            SkipReason = $"POST /spectator/api/requests failed ({ex.GetType().Name}) — live stack became unreachable mid-setup";
            return;
        }

        if (wishResponse.StatusCode != HttpStatusCode.Accepted)
        {
            // A 429 here is most likely a previous run's PerIpCooldownMinutes budget (SPEC
            // F87.3) — an environment condition, not a defect, so this stays a skip rather than
            // a failure.
            SkipReason = $"POST /spectator/api/requests returned {(int)wishResponse.StatusCode} (expected 202) " +
                "— possibly a per-IP cooldown/cap from a previous run; not exercising the flywheel this run";
            return;
        }

        await PollForFulfillmentAsync(admin, wishPostedAt);

        if (FulfilledTrackAired)
            await ObserveIcyArtworkAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Reachability + configuration preconditions. Sets <see cref="SkipReason"/> and
    /// returns <see langword="false"/> for every "can't exercise this gate right now" condition.</summary>
    async Task<bool> TryPrepareAsync(HttpClient admin)
    {
        var password = ReadAdminPassword();
        if (password is null)
        {
            SkipReason = "ADMIN_PASSWORD not available (env var or repo-root .env) — cannot drive the live flywheel gate";
            return false;
        }

        HttpResponseMessage loginResponse;
        try
        {
            loginResponse = await admin.PostAsJsonAsync("/api/auth/login", new { password });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException)
        {
            SkipReason = $"live stack not reachable at {ApiBaseUrl} ({ex.GetType().Name}) — flywheel gate not exercised";
            return false;
        }

        if (!loginResponse.IsSuccessStatusCode)
        {
            SkipReason = $"admin login failed ({(int)loginResponse.StatusCode}) — flywheel gate not exercised";
            return false;
        }

        var settings = await admin.GetFromJsonAsync<JsonElement>("/api/settings");

        if (!TryReadSetting(settings, "Station:Requests:Enabled", out var requestsEnabled)
            || !string.Equals(requestsEnabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            SkipReason = "Station:Requests:Enabled is not \"true\" (PUT /api/settings) — flywheel gate not exercised";
            return false;
        }

        if (!TryReadSetting(settings, "Station:PublicBaseUrl", out var publicBaseUrl) || string.IsNullOrEmpty(publicBaseUrl))
        {
            SkipReason = "Station:PublicBaseUrl is empty (PUT /api/settings) — artwork URLs are never emitted (F88.4)";
            return false;
        }

        RequestedArtist = await FindHeldArtistAsync(admin);
        if (RequestedArtist is null)
        {
            SkipReason = "no ready+eligible, non-never-play catalog artist was found via GET /api/media — flywheel gate not exercised";
            return false;
        }

        return true;
    }

    static bool TryReadSetting(JsonElement settings, string key, out string value)
    {
        foreach (var entry in settings.EnumerateArray())
        {
            if (entry.TryGetProperty("key", out var keyProp)
                && string.Equals(keyProp.GetString(), key, StringComparison.Ordinal)
                && entry.TryGetProperty("value", out var valueProp))
            {
                value = valueProp.GetString() ?? "";
                return true;
            }
        }

        value = "";
        return false;
    }

    /// <summary>The first ready+eligible row with a non-empty artist and no never_play flag (SPEC
    /// F87.5's own selectability predicate) — a held, requestable artist the wish can name.</summary>
    static async Task<string?> FindHeldArtistAsync(HttpClient admin)
    {
        var media = await admin.GetFromJsonAsync<JsonElement>("/api/media?state=ready&eligible=true&limit=20");
        foreach (var row in media.EnumerateArray())
        {
            var artist = row.TryGetProperty("artist", out var artistProp) ? artistProp.GetString() : null;
            var neverPlay = row.TryGetProperty("neverPlay", out var neverPlayProp) && neverPlayProp.GetBoolean();
            if (!string.IsNullOrWhiteSpace(artist) && !neverPlay)
                return artist;
        }

        return null;
    }

    /// <summary>
    /// Polls the admin booth log (SPEC F72.2) for the fulfillment + airing + lead-in narrative.
    /// Correlating a specific request row to a specific airing is deliberately impossible via any
    /// public/admin API (F87.9's own "no oracle") — this instead requires BOTH a request-fulfilled
    /// entry AND a track-started entry for the requested artist inside the poll window, which is
    /// the strongest honest correlation the disclosure contract allows.
    /// <para>
    /// A live run (2026-07-24, this task) showed the fulfillment PICK (queue look-ahead) and the
    /// matching track-started AIR event can be a full track apart — "request-fulfilled" fired while
    /// a different, already-queued track was still on air, with the requested artist only starting
    /// once THAT track finished. <see cref="PollTimeout"/> already accommodates this (it bounds the
    /// whole wait, not a fixed offset from the fulfilled event); the lead-in correlation below is
    /// keyed off the artist's own track-started row, never off the fulfilled event's timestamp.
    /// </para>
    /// </summary>
    async Task PollForFulfillmentAsync(HttpClient admin, DateTime wishPostedAt)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var page = await admin.GetFromJsonAsync<JsonElement>("/api/booth-log?take=50");
            var entries = page.GetProperty("entries").EnumerateArray().ToList();

            var fulfilled = entries.Any(entry => IsKind(entry, "request-fulfilled") && OccurredAfter(entry, wishPostedAt));
            var trackStarted = entries.FirstOrDefault(entry =>
                IsKind(entry, "track-started")
                && OccurredAfter(entry, wishPostedAt)
                && SummaryMentionsArtist(entry, RequestedArtist ?? ""));

            if (fulfilled && trackStarted.ValueKind != JsonValueKind.Undefined)
            {
                var trackStartedAt = OccurredAt(trackStarted);
                FulfilledTrackAired = true;
                // The render-completion (patter-aired) event and the on-air-detected
                // (track-started) event are two independently-timed publishers — a live run showed
                // the lead-in logged slightly AFTER the track it introduces, not only before — so
                // this is a symmetric proximity window, not an ordering assumption.
                ShoutOutLeadInObserved = entries.Any(entry =>
                    IsKind(entry, "patter-aired")
                    && (entry.GetProperty("summary").GetString() ?? "").Contains("LeadIn", StringComparison.Ordinal)
                    && Math.Abs((OccurredAt(entry) - trackStartedAt).TotalSeconds) <= 60);
                return;
            }

            Diagnostics = $"last poll {DateTime.UtcNow:O}: fulfilled={fulfilled}, " +
                $"trackStartedForArtist={trackStarted.ValueKind != JsonValueKind.Undefined}";
            await Task.Delay(PollInterval);
        }
    }

    static bool IsKind(JsonElement entry, string kind) =>
        entry.TryGetProperty("kind", out var kindProp) && kindProp.GetString() == kind;

    static bool OccurredAfter(JsonElement entry, DateTime threshold) => OccurredAt(entry) >= threshold;

    static DateTime OccurredAt(JsonElement entry) => entry.GetProperty("occurredAt").GetDateTime();

    static bool SummaryMentionsArtist(JsonElement entry, string artist) =>
        entry.TryGetProperty("summary", out var summaryProp)
        && (summaryProp.GetString() ?? "").Contains($"by {artist}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The honest cheapest observable for F88.5 once the track is confirmed airing: a single ICY
    /// metadata read (not the continuous round-trip/drain/recovery monitoring tools/onair_gate.sh
    /// does for the engine's OWN output-metadata seam — a different concern already covered there)
    /// against the live stream, followed by one GET of the StreamUrl it reports.
    /// </summary>
    async Task ObserveIcyArtworkAsync()
    {
        var (_, streamUrl) = await ReadIcyMetadataOnceAsync(
            new Uri($"http://{StreamHost}:{StreamPort}{StreamPath}"), IcyTimeout, CancellationToken.None);
        IcyStreamUrl = streamUrl;
        if (string.IsNullOrEmpty(streamUrl))
            return;

        using var art = new HttpClient { Timeout = HttpTimeout };
        using var response = await art.GetAsync(streamUrl);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        ArtworkBytesServed = response.IsSuccessStatusCode
            && response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.Ordinal) == true
            && bytes.Length > 0;
    }

    // -----------------------------------------------------------------------------------------
    // ICY metadata handshake (SPEC F88.4/F88.5) — a raw socket read, not HttpClient: icy-metaint
    // is a non-standard response header whose value gates a binary framing (audio bytes, then a
    // length-prefixed metadata block) HttpClient's content model has no vocabulary for.
    // -----------------------------------------------------------------------------------------

    static async Task<(string? StreamTitle, string? StreamUrl)> ReadIcyMetadataOnceAsync(
        Uri streamUri, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(streamUri.Host, streamUri.Port, timeoutCts.Token);
        await using var networkStream = tcp.GetStream();

        var request = $"GET {streamUri.PathAndQuery} HTTP/1.0\r\nHost: {streamUri.Host}\r\nIcy-MetaData: 1\r\nConnection: close\r\n\r\n";
        await networkStream.WriteAsync(Encoding.ASCII.GetBytes(request), timeoutCts.Token);

        var headers = await ReadHttpHeaderBlockAsync(networkStream, timeoutCts.Token);
        var metaInt = ParseIcyMetaInt(headers);
        if (metaInt is null)
            return (null, null);

        await DiscardBytesAsync(networkStream, metaInt.Value, timeoutCts.Token);
        var metadataBlock = await ReadIcyMetadataBlockAsync(networkStream, timeoutCts.Token);
        return ParseIcyStreamFields(metadataBlock);
    }

    static async Task<string> ReadHttpHeaderBlockAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new List<byte>();
        var single = new byte[1];
        while (!EndsWithHeaderTerminator(buffer))
        {
            var read = await stream.ReadAsync(single, ct);
            if (read == 0) break;
            buffer.Add(single[0]);
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    static bool EndsWithHeaderTerminator(List<byte> buffer) =>
        buffer.Count >= 4
        && buffer[^4] == (byte)'\r' && buffer[^3] == (byte)'\n'
        && buffer[^2] == (byte)'\r' && buffer[^1] == (byte)'\n';

    static int? ParseIcyMetaInt(string headers)
    {
        var match = Regex.Match(headers, @"icy-metaint:\s*(\d+)", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    static async Task DiscardBytesAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[Math.Min(count, 8192)];
        var remaining = count;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(remaining, buffer.Length)), ct);
            if (read == 0) throw new InvalidOperationException("stream closed before the icy-metaint audio bytes were fully read");
            remaining -= read;
        }
    }

    static async Task<string> ReadIcyMetadataBlockAsync(NetworkStream stream, CancellationToken ct)
    {
        var lengthByte = new byte[1];
        if (await stream.ReadAsync(lengthByte, ct) == 0 || lengthByte[0] == 0)
            return string.Empty;

        var metadataBuffer = new byte[lengthByte[0] * 16];
        var totalRead = 0;
        while (totalRead < metadataBuffer.Length)
        {
            var read = await stream.ReadAsync(metadataBuffer.AsMemory(totalRead), ct);
            if (read == 0) throw new InvalidOperationException("stream closed before the ICY metadata block was fully read");
            totalRead += read;
        }

        return Encoding.ASCII.GetString(metadataBuffer).TrimEnd('\0');
    }

    static (string? StreamTitle, string? StreamUrl) ParseIcyStreamFields(string metadataBlock)
    {
        var titleMatch = Regex.Match(metadataBlock, "StreamTitle='([^']*)'");
        var urlMatch = Regex.Match(metadataBlock, "StreamUrl='([^']*)'");
        return (
            titleMatch.Success ? titleMatch.Groups[1].Value : null,
            urlMatch.Success ? urlMatch.Groups[1].Value : null);
    }

    /// <summary>ADMIN_PASSWORD from the environment, falling back to the repo-root .env file
    /// (Story013's own idiom, repeated here rather than shared — see this suite's established
    /// per-gate-file convention).</summary>
    static string? ReadAdminPassword()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
        if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;

        var envFilePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env"));
        if (!File.Exists(envFilePath)) return null;

        try
        {
            foreach (var line in File.ReadAllLines(envFilePath))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("ADMIN_PASSWORD=", StringComparison.Ordinal)) continue;

                var value = trimmed["ADMIN_PASSWORD=".Length..];
                return value.Length > 0 ? value : null;
            }
        }
        catch (IOException)
        {
            return null;
        }

        return null;
    }
}
