using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// The booth log's admin-only paged feed (SPEC F72.2, STORY-195) — "what did it say at 9:14" is
/// answerable from this endpoint alone. Never on any spectator/public surface (F72.4): no
/// <see cref="SpectatorSurfaceAttribute"/>, deny-by-default like every other admin route.
///
/// Also serves the taste-thumb accrual endpoint (SPEC F84.1, F84.5, F84.6; STORY-215, PLAN T70):
/// <c>POST /api/booth-log/{id}/taste-thumb</c>. One route shape covers BOTH the now-playing and
/// booth-log admin surfaces — the credited persona is whichever one is stamped on the booth-log row
/// itself (F84.1), never whichever persona happens to be active now, so a now-playing thumb is
/// simply "resolve to the latest track-start booth-log row, then call this same route" (T71's job;
/// no second endpoint shape exists for it to diverge from this one).
/// </summary>
[ApiController]
[Route("api/booth-log")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class BoothLogController(IBoothLogReader store, IPersonaTasteAccrualStore accrual) : ControllerBase
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
            page.Entries.Select(e => new BoothLogEntryDto(e.Id, e.OccurredAt, e.Kind, e.Summary, e.PersonaId)).ToList(),
            page.NextBefore?.ToString()));
    }

    /// <summary>
    /// POST /api/booth-log/{id}/taste-thumb — nudge the accrued artist rule for whichever persona was
    /// stamped on booth-log row <paramref name="id"/> at air time (SPEC F84.1, F84.6). Body:
    /// <c>{ "direction": "up" | "down" }</c> (case-insensitive, mirrors <see cref="RatingController.Vote"/>'s
    /// own parsing). Invalid direction → 400, nothing written. Unknown row id → 404. A row with no
    /// persona stamp, not a track-start row, or no known artist → 400 (F84.6, not thumbable). A
    /// repeat thumb for the same (persona, row, direction) → 200, idempotent no-op (F84.5).
    /// </summary>
    [HttpPost("{id:long}/taste-thumb")]
    [Consumes("application/json")]
    public async Task<IActionResult> ThumbTaste(long id, [FromBody] TasteThumbRequest request, CancellationToken ct)
    {
        if (!TryParseDirection(request.Direction, out var direction))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Invalid direction.",
                Detail = "direction must be \"up\" or \"down\".",
            });
        }

        var outcome = await accrual.ThumbAsync(id, direction, ct);

        return outcome switch
        {
            TasteThumbOutcome.Nudged nudged => Ok(new TasteThumbResponse(AlreadyRecorded: false, nudged.Weight)),
            TasteThumbOutcome.AlreadyRecorded => Ok(new TasteThumbResponse(AlreadyRecorded: true, Weight: null)),
            TasteThumbOutcome.RowNotFound => NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title  = "Not found.",
                Detail = $"No booth-log row with id {id} exists.",
            }),
            TasteThumbOutcome.NotThumbable => BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Not thumbable.",
                Detail = $"Booth-log row {id} has no persona stamp, is not a track-start row, or has no known artist (F84.6).",
            }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>Case-insensitive match against the only two valid direction values — mirrors <c>RatingController</c>'s own.</summary>
    static bool TryParseDirection(string? direction, out TasteThumbDirection parsed)
    {
        switch (direction?.Trim().ToLowerInvariant())
        {
            case "up":
                parsed = TasteThumbDirection.Up;
                return true;
            case "down":
                parsed = TasteThumbDirection.Down;
                return true;
            default:
                parsed = default;
                return false;
        }
    }
}
