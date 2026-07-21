using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// The booth log's admin-only paged feed (SPEC F72.2, STORY-195) — "what did it say at 9:14" is
/// answerable from this endpoint alone. Never on any spectator/public surface (F72.4): no
/// <see cref="SpectatorSurfaceAttribute"/>, deny-by-default like every other admin route.
/// </summary>
[ApiController]
[Route("api/booth-log")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class BoothLogController(IBoothLogReader store) : ControllerBase
{
    const int DefaultTake = 50;
    const int MaxTake = 200;

    /// <summary>
    /// GET /api/booth-log?before=&amp;take= — newest-first keyset page (SPEC F72.2). <c>before</c> is
    /// the opaque cursor from a previous response's <c>nextBefore</c> (absent = the newest page);
    /// <c>take</c> is clamped to [1, 200] (default 50). 400 for a malformed <c>before</c>.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? before, [FromQuery] int? take, CancellationToken ct)
    {
        BoothLogCursor? cursor = null;
        if (!string.IsNullOrWhiteSpace(before) && !BoothLogCursor.TryParse(before, out cursor))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Validation error.",
                Detail = "before is not a valid cursor.",
            });
        }

        var effectiveTake = take is null or <= 0 ? DefaultTake : Math.Min(take.Value, MaxTake);

        var page = await store.ReadAsync(cursor, effectiveTake, ct);

        return Ok(new BoothLogPageDto(
            page.Entries.Select(e => new BoothLogEntryDto(e.OccurredAt, e.Kind, e.Summary, e.PersonaId)).ToList(),
            page.NextBefore?.ToString()));
    }
}
