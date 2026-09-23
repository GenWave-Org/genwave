using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GenWave.Host.Api;

/// <summary>
/// GET /api/attributions (SPEC F169.2, STORY-404, PLAN T419) — a thin wrapper over
/// <see cref="AttributionProjector"/>, which both this controller and <see cref="AboutController"/>
/// (SPEC F207.2, PLAN T561) call — see that type's own remarks for the full projection contract.
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Curation)]
public sealed class AttributionsController(AttributionProjector attributionProjector) : ControllerBase
{
    /// <summary>GET /api/attributions (SPEC F169.2) — see <see cref="AttributionProjector"/>'s own
    /// remarks for the full projection contract.</summary>
    [HttpGet("attributions")]
    public async Task<IActionResult> GetAttributions(CancellationToken ct) =>
        Ok(await attributionProjector.BuildAsync(ct));
}
