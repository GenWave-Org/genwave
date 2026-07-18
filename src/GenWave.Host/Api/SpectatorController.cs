using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
/// absorption (SPEC F62.3, added in a later task alongside <c>OutputCache</c>/rate limiting).
/// </para>
/// </summary>
[ApiController]
[Route("spectator/api")]
[SpectatorSurface]
[Authorize(Policy = AuthorizationPolicies.Spectator)]
public sealed class SpectatorController(NowPlayingService nowPlayingService) : ControllerBase
{
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
    public IActionResult GetNowPlaying()
    {
        var snapshot = nowPlayingService.GetSnapshot(SingleStation.IdString);

        if (snapshot is null || snapshot.IsDrain)
            return Ok(new SpectatorStandbyNowPlaying());

        if (snapshot.MediaId is { } mediaId && mediaId.StartsWith("tts:", StringComparison.Ordinal))
            return Ok(new SpectatorPatterNowPlaying(snapshot.StartedAt, snapshot.DurationMs));

        return Ok(new SpectatorTrackNowPlaying(snapshot.Title, snapshot.Artist, snapshot.StartedAt, snapshot.DurationMs));
    }
}
