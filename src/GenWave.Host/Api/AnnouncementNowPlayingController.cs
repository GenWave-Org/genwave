using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using GenWave.Host.Auth;
using GenWave.Host.Playout;

namespace GenWave.Host.Api;

/// <summary>
/// <c>GET /api/announcements/now-playing</c> (SPEC F145.3/.6, STORY-360, STORY-366, PLAN T340/T351)
/// — the token-reachable now-playing read F147.3's home-automation sensor consumes. Split out of
/// <see cref="AnnouncementsController"/> at T351 to give it that controller's real submit/history
/// endpoints' <see cref="AdminSurfaceAttribute"/> gate WITHOUT it: SPEC F145.6 makes this one route
/// the family's sole exception — it answers whenever a token exists, admin plane on or off, public
/// or private station — because the appliance topology (<c>Admin:Enabled=false</c>) still needs the
/// sensor's read to work; a fail-closed 401 on "no token row" already covers the privacy floor
/// (<c>AnnounceTokenAuthenticationHandler</c>'s own fail-closed contract, unchanged by this move).
///
/// <para>
/// <b>No <see cref="AdminSurfaceAttribute"/> — deliberately, the whole point of this file.</b> Every
/// other Operator-plane controller in this project carries it; this one does not, so
/// <see cref="SurfaceGateMiddleware"/> never 404s this route regardless of <c>Admin:Enabled</c>.
/// Existence is unconditional; REACHABILITY still is not — the
/// <see cref="AnnounceTokenAuthenticationDefaults.InScopeSchemes"/> authorization immediately below
/// still fails closed with no configured token (SPEC F145.4's "no hash row" state), so a public
/// appliance with no token ever minted answers 401, never a snapshot.
/// </para>
///
/// <para>
/// <b>Same scheme list as <see cref="AnnouncementsController"/>, a SECOND carrier by design.</b>
/// Accepts EITHER the admin cookie session OR the announce Bearer token — the same Operator-plane
/// grouping <see cref="AnnouncementsController"/>'s own remarks describe. The L9 architecture fence
/// (<c>AnnounceSchemeFence</c>, STORY-366 AC5) widens at T351 to name EXACTLY these two controllers
/// as the scheme's designated carriers; a third production type naming
/// <see cref="AnnounceTokenAuthenticationDefaults.SchemeName"/> in an
/// <see cref="AuthorizeAttribute.AuthenticationSchemes"/> list still fails the law.
/// </para>
///
/// <para>
/// <b>The per-IP door limiter stays (T351 review call).</b> <see cref="EnableRateLimitingAttribute"/>
/// still carries <see cref="RateLimiterPolicies.Announcements"/> — see that member's own remarks for
/// the full rationale, unchanged by this move: its entire purpose is bounding
/// <see cref="AnnounceTokenAuthenticationHandler"/>'s own <see cref="IAnnounceTokenStore.ReadHashAsync"/>
/// call, which fires on EVERY Bearer attempt (valid, invalid, or a junk flood) BEFORE this action —
/// or even authentication itself — ever runs, since the limiter is middleware, positioned upstream of
/// <c>UseAuthentication</c>. Dropping it here would leave exactly that pre-auth DB read unguarded on
/// the one announcements-family route SPEC F145.6 now makes reachable with NO admin-plane gate in
/// front of it at all — a strictly WORSE moment to remove a flood guard, not a better one, than when
/// <see cref="AdminSurfaceAttribute"/> still stood in front of it. The 60/minute ceiling is already
/// tuned for this exact caller: <see cref="RateLimiterPolicies.Announcements"/>'s own remarks name the
/// F147.3 sensor's ≥30s polling cadence (&lt;3 requests/minute at its fastest legal rate) as one of the
/// two legitimate callers this window was sized to never bite.
/// </para>
/// </summary>
[ApiController]
[Route("api/announcements/now-playing")]
[Authorize(AuthenticationSchemes = AnnounceTokenAuthenticationDefaults.InScopeSchemes, Policy = AuthorizationPolicies.Operator)]
[EnableRateLimiting(RateLimiterPolicies.Announcements)]
public sealed class AnnouncementNowPlayingController(NowPlayingService nowPlayingService) : ControllerBase
{
    /// <summary>
    /// Reuses the SAME in-memory <see cref="NowPlayingService"/> read <see cref="LiveController.GetNowPlaying"/>
    /// and <see cref="SpectatorController.GetNowPlaying"/> already use — no engine telnet call, no DB
    /// read, no new poller — projected down to <see cref="AnnouncementNowPlayingDto"/>'s minimal shape
    /// (see that record's own remarks for why it carries only title/artist/DJ name). No SpectatorMode
    /// gate here (unlike <see cref="AnnouncementsController.Post"/>): a public station's privacy floor
    /// is the token door's own fail-closed contract (this class's own remarks), never a spectator-mode
    /// check this action would otherwise have to duplicate.
    /// </summary>
    [HttpGet]
    public IActionResult NowPlaying()
    {
        var snapshot = nowPlayingService.GetSnapshot(SingleStation.IdString);
        if (snapshot is null || snapshot.IsDrain)
            return Ok(new AnnouncementNowPlayingDto(null, null, null));

        return Ok(new AnnouncementNowPlayingDto(snapshot.Title, snapshot.Artist, snapshot.DjName));
    }
}
