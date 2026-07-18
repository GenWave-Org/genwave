using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Track rating endpoints for the Admin UI (SPEC F33, closes gitea-#188): a vote
/// (<c>POST .../vote</c>), a never-play toggle (<c>PUT .../never-play</c>), and a batch read
/// (<c>GET /api/ratings</c>) over <see cref="IMediaRating"/>.
///
/// DELIBERATE F23.4 DEVIATION (SPEC F33.5) — DO NOT ADD SCOPE CHECKS HERE. Every endpoint below
/// operates on any catalog row by id with NO <see cref="IStationScopeProvider"/> /
/// <c>Station:Scope:LibraryIds</c> gating, unlike <see cref="MediaController"/>'s single-row 403
/// rule (F23.4). Rationale (the gitea-#203-trap that this deliberately avoids reintroducing): the Live
/// page surfaces plays from ANY scope, and safe-loop plays routinely air OUTSIDE main scope on the
/// default deploy shape — a scope-gated vote/X control would 403 on exactly the rows an operator
/// most wants to rate. Rating is standalone from curation (F33.7): it is a per-row concern, not a
/// rotation-scope one. A reviewer seeing scope checks reintroduced into this controller should
/// fail the change.
///
/// No <c>If-Match</c>/ETag anywhere in this controller: a vote is an atomic clamped increment and
/// a never-play set is idempotent (F33.3/F33.4) — neither has anything to conflict on, and neither
/// ever touches <c>library.media</c>'s <c>xmin</c> (F33.1), so an open PATCH form's ETag survives
/// any number of votes.
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class RatingController(IMediaRating rating) : ControllerBase
{
    /// <summary>
    /// POST /api/media/{id}/vote — apply one ±1 vote, clamped to <c>[0,100]</c> (F33.3).
    /// Body: <c>{ "direction": "up" | "down" }</c> (case-insensitive). Invalid direction → 400,
    /// nothing written. Unknown id → 404. Success → 200 <c>{ score }</c>.
    /// </summary>
    [HttpPost("media/{id:long}/vote")]
    [Consumes("application/json")]
    public async Task<IActionResult> Vote(
        long id,
        [FromBody] VoteRequest request,
        CancellationToken ct)
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

        var outcome = await rating.VoteAsync(
            id.ToString(System.Globalization.CultureInfo.InvariantCulture), direction, ct);

        return outcome.Result switch
        {
            RatingWriteResult.Updated  => Ok(new { score = outcome.Score }),
            RatingWriteResult.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// PUT /api/media/{id}/never-play — idempotently set the never-play flag (F33.4).
    /// Body: <c>{ "neverPlay": bool }</c>. Unknown id → 404. Success → 200 <c>{ neverPlay }</c>.
    /// </summary>
    [HttpPut("media/{id:long}/never-play")]
    [Consumes("application/json")]
    public async Task<IActionResult> SetNeverPlay(
        long id,
        [FromBody] NeverPlayRequest request,
        CancellationToken ct)
    {
        var outcome = await rating.SetNeverPlayAsync(
            id.ToString(System.Globalization.CultureInfo.InvariantCulture), request.NeverPlay, ct);

        return outcome.Result switch
        {
            RatingWriteResult.Updated  => Ok(new { neverPlay = outcome.NeverPlay }),
            RatingWriteResult.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// GET /api/ratings?ids=1,2,tts:x — batch rating read (F33.2, F33.9). The comma-separated
    /// list is passed through to <see cref="IMediaRating.GetRatingsAsync"/> unchanged — the
    /// repository already skips non-numeric ids (e.g. <c>tts:*</c>) and resolves ledger defaults
    /// for parseable ids with no rating row; this endpoint adds no filtering of its own.
    /// </summary>
    [HttpGet("ratings")]
    public async Task<IActionResult> GetRatings([FromQuery] string? ids, CancellationToken ct)
    {
        var mediaIds = (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var ratings = await rating.GetRatingsAsync(mediaIds, ct);

        return Ok(ratings);
    }

    /// <summary>Case-insensitive match against the only two valid direction values (F33.3).</summary>
    static bool TryParseDirection(string? direction, out VoteDirection parsed)
    {
        switch (direction?.Trim().ToLowerInvariant())
        {
            case "up":
                parsed = VoteDirection.Up;
                return true;
            case "down":
                parsed = VoteDirection.Down;
                return true;
            default:
                parsed = default;
                return false;
        }
    }
}
