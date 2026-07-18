using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Options;
using GenWave.Host.Playout;

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
    IOptionsMonitor<StationOptions> stationMonitor) : ControllerBase
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
    /// </summary>
    [HttpGet("now-playing")]
    [OutputCache(PolicyName = SpectatorOutputCachePolicies.NowPlaying)]
    [SpectatorCacheControl(5)]
    public IActionResult GetNowPlaying()
    {
        var snapshot = nowPlayingService.GetSnapshot(SingleStation.IdString);

        if (snapshot is null || snapshot.IsDrain)
            return Ok(new SpectatorStandbyNowPlaying());

        if (snapshot.MediaId is { } mediaId && mediaId.StartsWith("tts:", StringComparison.Ordinal))
            return Ok(new SpectatorPatterNowPlaying(snapshot.StartedAt, snapshot.DurationMs));

        return Ok(new SpectatorTrackNowPlaying(snapshot.Title, snapshot.Artist, snapshot.StartedAt, snapshot.DurationMs));
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
    [OutputCache(PolicyName = SpectatorOutputCachePolicies.Stats)]
    [SpectatorCacheControl(30)]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var safeScope = new LibraryScope(stationMonitor.CurrentValue.SafeScope.LibraryIds.ToArray());
        var counts = await catalog.GetStatusCountsAsync(safeScope, ct);

        return Ok(new SpectatorStats(counts.Ready, counts.Enriching, counts.Failed));
    }

    /// <summary>
    /// GET /spectator/api/about — the public identity panel (SPEC F62.8, F65.3): station name and
    /// public stream URL read live from <see cref="StationOptions"/>, alongside the build-stamped
    /// version, license, and canonical project URL, which cannot change at runtime.
    /// </summary>
    [HttpGet("about")]
    [OutputCache(PolicyName = SpectatorOutputCachePolicies.About)]
    [SpectatorCacheControl(300)]
    public IActionResult GetAbout()
    {
        var options = stationMonitor.CurrentValue;
        return Ok(new SpectatorAbout(options.Name, HostVersion, License, ProjectUrl, options.PublicStreamUrl));
    }

    static object ToPublicEntry(PlayHistoryEntry entry) =>
        entry.MediaId.StartsWith("tts:", StringComparison.Ordinal)
            ? new SpectatorPlayHistoryPatterEntry(entry.StartedAt)
            : new SpectatorPlayHistoryTrackEntry(entry.Title, entry.Artist, entry.StartedAt);
}
