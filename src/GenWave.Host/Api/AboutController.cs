using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// GET /api/about (SPEC F207.2, STORY-474, PLAN T561) — the About page's own facts (SPEC F207.1) in
/// one round-trip: the build-stamped version, station name + tagline, library count, process uptime,
/// and the F169 attribution list. Curation policy — the SAME policy <c>GET /api/attributions</c>
/// carries — because this response embeds that exact attribution payload; widening who can read
/// <c>/api/about</c> would widen who can read it too.
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Curation)]
public sealed class AboutController(
    IAdminMediaQuery adminQuery,
    IOptionsMonitor<StationOptions> stationMonitor,
    ProcessStartTime startTime,
    TimeProvider timeProvider,
    AttributionProjector attributionProjector) : ControllerBase
{
    /// <summary>
    /// GET /api/about. <c>version</c>/<c>stationName</c>/<c>tagline</c> read exactly as
    /// <see cref="SpectatorController.GetAbout"/> does (<see cref="HostVersion.Value"/>,
    /// <see cref="IOptionsMonitor{StationOptions}.CurrentValue"/>) — live, per request, so a
    /// <c>PUT /api/settings</c> edit to <c>Station:Name</c>/<c>Station:Tagline</c> reaches the very
    /// next call with no api restart. <c>tagline</c> is <c>""</c> when unset (SPEC's own "the page
    /// renders it only when non-blank" ruling lives on the page, not here).
    /// <para>
    /// <c>uptimeSeconds</c> is whole seconds between <see cref="ProcessStartTime.Value"/> (the
    /// process's true boot instant, captured once at Program.cs startup — see that type's own
    /// remarks) and <see cref="TimeProvider.GetUtcNow"/> — the SAME injected <see cref="TimeProvider"/>
    /// singleton every other clock-reading Host component resolves, never <see cref="DateTimeOffset.UtcNow"/>
    /// directly, so a future test can fake this endpoint's notion of "now" exactly like it fakes any
    /// other.
    /// </para>
    /// <para>
    /// <c>libraryCount</c> is <see cref="IAdminMediaQuery.GetReadyMusicCountAsync"/> — rows in
    /// <c>library.media</c> with <c>state = 'ready' and imaging_kind is null</c>, i.e. actual MUSIC
    /// tracks ready to air. Deliberately NOT <see cref="CatalogStatusCounts.Ready"/> (the
    /// <c>GET /api/status</c>/<c>GET /api/libraries</c> figure): that count includes every authored
    /// imaging row (liner/station_id/jingle/promo/ad) too, which would misreport "tracks in the
    /// library" as inflated by station patter and ads rather than music the reader can actually put a
    /// name to. Unscoped across the whole catalog, mirroring <c>Ready</c>'s own unscoped state count
    /// (F20.1) rather than a <see cref="LibraryScope"/>-narrowed figure.
    /// </para>
    /// <para>
    /// <c>attributions</c> is <see cref="AttributionProjector.BuildAsync"/>'s own result — the
    /// identical <see cref="AttributionsResponse"/> <c>GET /api/attributions</c> serves, both
    /// controllers calling the SAME injected projector so the two can never independently drift
    /// (STORY-474 AC3).
    /// </para>
    /// </summary>
    [HttpGet("about")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var options = stationMonitor.CurrentValue;
        var libraryCount = await adminQuery.GetReadyMusicCountAsync(ct);
        var attributions = await attributionProjector.BuildAsync(ct);
        var uptimeSeconds = (long)(timeProvider.GetUtcNow() - startTime.Value).TotalSeconds;

        return Ok(new AboutResponse(
            HostVersion.Value, options.Name, options.Tagline, libraryCount, uptimeSeconds, attributions));
    }
}
