using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Host.Stats;

namespace GenWave.Host.Api;

/// <summary>
/// Container-level view of the running stack for the admin Health page (gh-#148) — pretty
/// `docker stats` without the admin UI (or the browser) ever holding a path to the Docker socket:
/// the api asks the allowlisted <c>dockerproxy</c> sidecar over the internal <c>stats</c> network
/// and serves the result here, behind the same cookie auth as every other admin surface.
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class HealthContainersController(DockerContainerStatsSource statsSource) : ControllerBase
{
    /// <summary>
    /// GET /api/health/containers — see <see cref="ContainerStatsReportDto"/> for the envelope and
    /// <see cref="DockerContainerStatsSource"/> for the degrade rules. Always 200; a missing
    /// sidecar is a degraded payload, never a 500.
    /// </summary>
    [HttpGet("health/containers")]
    public async Task<ContainerStatsReportDto> Get(CancellationToken ct) =>
        await statsSource.GetReportAsync(ct);
}
