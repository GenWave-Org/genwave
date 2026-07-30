using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Options;
using GenWave.Host.Playout;
using GenWave.Orchestration;

namespace GenWave.Host.Api;

/// <summary>
/// The public read-only spectator surface (SPEC F62). A controller, matching the admin API's
/// existing convention (see <see cref="LiveController"/>) — one style per area, not a second
/// minimal-API shape introduced alongside it (POLA).
/// <para>
/// Carries both gates every spectator endpoint needs: <see cref="SpectatorSurfaceAttribute"/> so
/// <see cref="SurfaceGateMiddleware"/> 404s the whole group when <c>Station:SpectatorMode</c> is
/// off (F62.2) — independently of <c>Admin:Enabled</c>, so the surface survives the admin kill
/// switch (STORY-166) — and <see cref="AuthorizationPolicies.Spectator"/>, which demands nothing
/// (SPEC F60.2), so the group stays reachable without a cookie.
/// </para>
/// <para>
/// Routes live under <c>/spectator/api/*</c>, deliberately outside <c>/api/*</c>: this keeps
/// <see cref="NoCacheApiMiddleware"/> (which only stamps <c>/api/*</c>) from fighting the public
/// <c>Cache-Control: public, max-age=N</c> headers this surface needs for CDN/reverse-proxy
/// absorption (SPEC F62.3/F62.10/F62.11, STORY-171/T13 — see
/// <see cref="SpectatorOutputCachePolicies"/> and <see cref="SpectatorCacheControlAttribute"/>).
/// </para>
/// <para>
/// <see cref="RateLimiterPolicies.Spectator"/> (SPEC F62.11) is applied class-wide: 120
/// requests/minute per source IP, upstream of <c>OutputCache</c> in the pipeline (Program.cs) so
/// a cached hit still counts against a caller's budget.
/// </para>
/// </summary>
[ApiController]
[Route("spectator/api")]
[SpectatorSurface]
[Authorize(Policy = AuthorizationPolicies.Spectator)]
[EnableRateLimiting(RateLimiterPolicies.Spectator)]
public sealed class SpectatorController(
    NowPlayingService nowPlayingService,
    PlayHistoryService playHistoryService,
    IMediaCatalog catalog,
    IListenerStatsSource listenerStats,
    IOptionsMonitor<StationOptions> stationMonitor,
    CachingScheduleResolver scheduleResolver,
    IActivePersonaAccessor personaAccessor,
    IRequestCatalogProbe requestCatalogProbe) : ControllerBase
{
    /// <summary>Hard cap on <c>GET /spectator/api/play-history</c> entries (SPEC F62.6), independent
    /// of the operator-configurable <c>Admin:PlayHistoryCapacity</c> ring size.</summary>
    const int MaxHistoryEntries = 20;

    /// <summary>SPDX identifier for the project's license (SPEC F62.8). The project is GPL-family,
    /// not operator-configurable — a literal, not a setting.</summary>
    const string License = "AGPL-3.0-or-later";

    /// <summary>Canonical public repository URL (SPEC F62.8), matching the one
    /// <see cref="GenWave.MediaLibrary.YearLookup.MusicBrainzYearLookup"/> sends as its User-Agent contact.</summary>
    const string ProjectUrl = "https://github.com/GenWave-Org/genwave";

    /// <summary>
    /// The build-stamped <see cref="AssemblyInformationalVersionAttribute"/> on the Host assembly
    /// (SPEC F65.1, STORY-175). Read once at class load — it is fixed for the process's lifetime,
    /// so re-reading it per request would only waste reflection.
    /// </summary>
    static readonly string HostVersion =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

    /// <summary>
    /// GET /spectator/api/now-playing — the public-shaped now-playing projection (SPEC F62.4/
    /// F62.5). A dedicated projection built from the same in-memory <see cref="NowPlayingService"/>
    /// read <see cref="LiveController"/> uses — no DB, no engine calls — but NEVER the admin DTO:
    /// media id, gain/loudness, and every other admin-only field are excluded by construction,
    /// not by filtering.
    /// <para>
    /// Always 200: feeder-warming (no snapshot yet) and safe-rotation drain both collapse to
    /// <c>{state:"standby"}</c> — the public never sees a 503 or the word "drain". TTS patter
    /// surfaces as <c>{state:"onAir", kind:"patter"}</c> with no title/artist properties at all
    /// (generated patter text and persona identity are operator content); a real track surfaces as
    /// <c>{state:"onAir", kind:"track", title, artist, startedAt, durationMs}</c>.
    /// </para>
    /// <para>
    /// Every shape also carries <c>listeners</c> (SPEC F62.12 addendum, STORY-179, gitea-#10) —
    /// always present, read fresh from <see cref="IListenerStatsSource"/>, null when Icecast's
    /// admin stats cannot be determined right now (never an error, never fabricated).
    /// </para>
    /// <para>
    /// Both on-air shapes also carry <c>dj</c> and <c>upNext</c> (SPEC F93.1/F93.2, STORY-244, PLAN
    /// T125) — <b>never</b> the standby shape, which stays exactly <c>{listeners, state}</c>.
    /// <c>dj</c> is read off the AIRING item's own snapshot (<see cref="NowPlayingSnapshot.DjName"/>,
    /// gh-#259) — never the schedule's live answer, which flips at a boundary while the engine queue
    /// can still be draining the previous show's rendered patters: the displayed DJ must name the
    /// voice/show actually on air, so it follows the item and flips only when the new schedule's
    /// items reach air. Null when the airing item was planned with no DJ on shift, or was never
    /// feeder-planned at all (safe rotation, engine-initiated). <c>upNext</c> stays schedule-truth —
    /// exactly one upcoming segment off <see cref="CachingScheduleResolver.TryGetCurrent"/> (no
    /// store round trip) and <see cref="IActivePersonaAccessor.TryGetCachedName"/> (no store round
    /// trip), collapsing to null under the SAME-PERSONA rule (see <see cref="SpectatorUpNext"/>'s
    /// own remarks) — F93.4's "no DB or engine call on the poll path" holds for both fields. Track
    /// state also carries <c>artworkUrl</c> (SPEC F93.3, STORY-245) straight off the snapshot —
    /// never a fresh per-poll lookup.
    /// </para>
    /// </summary>
    [HttpGet("now-playing")]
    [HttpHead("now-playing")]   // gh-#160: HEAD answers with GET's exact status/headers, body suppressed by the server
    [OutputCache(PolicyName = SpectatorOutputCachePolicies.NowPlaying)]
    [SpectatorCacheControl(5)]
    public async Task<IActionResult> GetNowPlaying(CancellationToken ct)
    {
        var snapshot = nowPlayingService.GetSnapshot(SingleStation.IdString);
        var listeners = await listenerStats.GetListenerCountAsync(ct);

        if (snapshot is null || snapshot.IsDrain)
            return Ok(new SpectatorStandbyNowPlaying(listeners));

        var dj = snapshot.DjName;
        var onAir = scheduleResolver.TryGetCurrent();
        var upNext = onAir is null ? null : ResolveUpNext(onAir);

        if (snapshot.MediaId is { } mediaId && mediaId.StartsWith("tts:", StringComparison.Ordinal))
            return Ok(new SpectatorPatterNowPlaying(snapshot.StartedAt, snapshot.DurationMs, listeners, dj, upNext));

        return Ok(new SpectatorTrackNowPlaying(
            snapshot.Title, snapshot.Artist, snapshot.StartedAt, snapshot.DurationMs, listeners,
            dj, upNext, snapshot.ArtworkUrl));
    }

    /// <summary>
    /// Projects <see cref="OnAirSnapshot.NextSegment"/>/<see cref="OnAirSnapshot.BoundaryAt"/> into
    /// the public <see cref="SpectatorUpNext"/> shape, or null when there is nothing to announce
    /// (SPEC F93.2) — see <see cref="SpectatorUpNext"/>'s own remarks for the full same-persona
    /// collapse rule this single comparison implements.
    /// </summary>
    SpectatorUpNext? ResolveUpNext(OnAirSnapshot onAir)
    {
        if (onAir.BoundaryAt is not { } boundaryAt) return null;
        if (onAir.NextSegment?.PersonaId == onAir.PersonaId) return null;

        var nextDj = onAir.NextSegment?.PersonaId is { } nextPersonaId
            ? personaAccessor.TryGetCachedName(nextPersonaId)
            : null;
        return new SpectatorUpNext(boundaryAt, nextDj);
    }

    /// <summary>
    /// GET /spectator/api/play-history — the public-shaped recent play history (SPEC F62.6), newest
    /// first, capped at <see cref="MaxHistoryEntries"/> regardless of the operator's configured ring
    /// size. Reads the same <see cref="PlayHistoryService"/> ring the admin surface uses — no DB
    /// round-trip — but projects each entry into one of two dedicated, unrelated shapes: a <c>tts:*</c>
    /// media id becomes <see cref="SpectatorPlayHistoryPatterEntry"/> (kind + airedAt only, anonymized
    /// per F62.9); anything else becomes <see cref="SpectatorPlayHistoryTrackEntry"/> (kind, title,
    /// artist, airedAt). No media id, gain/loudness, or duration ever appears — excluded by
    /// construction, not by filtering.
    /// </summary>
    [HttpGet("play-history")]
    [HttpHead("play-history")]   // gh-#160: HEAD answers with GET's exact status/headers, body suppressed by the server
    [OutputCache(PolicyName = SpectatorOutputCachePolicies.PlayHistory)]
    [SpectatorCacheControl(30)]
    public IActionResult GetPlayHistory()
    {
        var entries = playHistoryService.GetEntries(SingleStation.IdString)
            .Take(MaxHistoryEntries)
            .Select(ToPublicEntry)
            .ToList();

        return Ok(new SpectatorPlayHistoryResponse(entries));
    }

    /// <summary>
    /// GET /spectator/api/stats — exactly <c>{ready, enriching, failed}</c> (SPEC F62.7). Reads
    /// <see cref="IMediaCatalog.GetStatusCountsAsync"/> with the same <c>Station:SafeScope:LibraryIds</c>
    /// scope <see cref="StatusController.Get"/> passes, so the public number always agrees with the
    /// admin dashboard's <c>catalog</c> block. Deliberately omits <c>unavailable</c>/<c>playable</c> —
    /// both would disclose SafeScope sizing to the public — by returning a DTO that simply has no
    /// properties for them (F62.9 disclosure-by-construction).
    /// <para>
    /// No try/catch here: a catalog failure (DB down) bubbles as a bare 500 with no exception
    /// details middleware on this surface. A public page polling this every 30s is expected to just
    /// ignore a failed poll and retry on the next tick — better than fabricating zero counts, which
    /// would misreport an outage as an empty catalog.
    /// </para>
    /// </summary>
    [HttpGet("stats")]
    [HttpHead("stats")]   // gh-#160: HEAD answers with GET's exact status/headers, body suppressed by the server
    [OutputCache(PolicyName = SpectatorOutputCachePolicies.Stats)]
    [SpectatorCacheControl(30)]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var safeScope = new LibraryScope(stationMonitor.CurrentValue.SafeScope.LibraryIds.ToArray());
        var counts = await catalog.GetStatusCountsAsync(safeScope, ct);

        return Ok(new SpectatorStats(counts.Ready, counts.Enriching, counts.Failed));
    }

    /// <summary>
    /// GET /spectator/api/about — the public identity panel (SPEC F62.8, F65.3): station name,
    /// public stream URL, and the listener-requests toggle read live from <see cref="StationOptions"/>
    /// (the latter added by SPEC F87.11, STORY-229, PLAN T92 — see <see cref="SpectatorAbout.RequestsEnabled"/>),
    /// alongside the build-stamped version, license, and canonical project URL, which cannot change
    /// at runtime.
    /// </summary>
    [HttpGet("about")]
    [HttpHead("about")]   // gh-#160: HEAD answers with GET's exact status/headers, body suppressed by the server
    [OutputCache(PolicyName = SpectatorOutputCachePolicies.About)]
    [SpectatorCacheControl(300)]
    public IActionResult GetAbout()
    {
        var options = stationMonitor.CurrentValue;
        return Ok(new SpectatorAbout(
            options.Name, HostVersion, License, ProjectUrl, options.PublicStreamUrl, options.Requests.Enabled));
    }

    /// <summary>
    /// GET /spectator/api/request-options — the request form's pick lists (gh-#131): the distinct
    /// genres of request-eligible catalog rows (law + safe-scope applied inside
    /// <see cref="IRequestCatalogProbe.ListRequestableGenresAsync"/> — safe content's genres never
    /// leak) and <see cref="MoodVocabulary.Terms"/> verbatim. Genre-granularity disclosure only —
    /// see <see cref="SpectatorRequestOptions"/>'s own remarks. Lives on this read-only controller
    /// (not <see cref="SpectatorRequestsController"/>) so it carries exactly the sibling GETs'
    /// posture: <see cref="RateLimiterPolicies.Spectator"/> class-wide, OutputCache + public
    /// Cache-Control at the stats/play-history 30s tier (the list only moves on catalog changes),
    /// and NO <see cref="RequestsSurfaceAttribute"/> — like <c>about</c>'s <c>requestsEnabled</c>
    /// flag, it stays reachable while the write endpoint's kill switch is off.
    /// </summary>
    [HttpGet("request-options")]
    [HttpHead("request-options")]   // gh-#160: HEAD answers with GET's exact status/headers, body suppressed by the server
    [OutputCache(PolicyName = SpectatorOutputCachePolicies.RequestOptions)]
    [SpectatorCacheControl(30)]
    public async Task<IActionResult> GetRequestOptions(CancellationToken ct)
    {
        var genres = await requestCatalogProbe.ListRequestableGenresAsync(ct);
        return Ok(new SpectatorRequestOptions(genres, MoodVocabulary.Terms));
    }

    static object ToPublicEntry(PlayHistoryEntry entry) =>
        entry.MediaId.StartsWith("tts:", StringComparison.Ordinal)
            ? new SpectatorPlayHistoryPatterEntry(entry.StartedAt)
            : new SpectatorPlayHistoryTrackEntry(entry.Title, entry.Artist, entry.StartedAt);
}
