using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Bulk rating endpoints for the Catalog toolbar (SPEC F61, STORY-158, closes gitea-#233): a filtered
/// vote sweep (<c>POST .../bulk/vote</c>) and a filtered never-play sweep
/// (<c>POST .../bulk/never-play</c>) over <see cref="IMediaRating"/>.
///
/// DELIBERATE CONTRAST WITH <see cref="RatingController"/> (F61.3) — that controller's per-row
/// actions stay scope-EXEMPT (F33.5): a Live-page vote/X must reach a safe-loop play airing
/// outside the station's rotation scope, and that rationale is per-row-only. This controller's
/// BULK actions are the opposite: scope-BOUNDED, the shipped convention every other bulk write
/// endpoint follows (<see cref="MediaController.BulkReassign"/>,
/// <see cref="MediaController.BulkSetEligibility"/>, <see cref="ReenrichController.BulkReenrich"/>)
/// — a filter-driven sweep over an unbounded catalog is exactly the "curation, not the Live page"
/// case F61.3's rationale carves out. <see cref="RatingController"/> is untouched by this file
/// (F33.5 stands, STORY-158 hard rule) — a reviewer seeing scope checks added there, or this
/// controller's scope bound removed, should fail the change.
///
/// Kept as its own controller rather than new actions on <see cref="RatingController"/> — mirrors
/// <see cref="ReenrichController"/>'s precedent of a bulk write concern living apart from
/// <see cref="MediaController"/> — so the per-row <see cref="RatingController"/> gains no new
/// constructor dependency and its existing (fake-backed) Story112 test suite needs no change.
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class BulkRatingController(
    IMediaRating rating,
    IStationScopeProvider scopeProvider,
    ILogger<BulkRatingController> logger) : ControllerBase
{
    /// <summary>
    /// POST /api/media/bulk/vote — apply one ±1 vote, clamped to <c>[0,100]</c>, to every row
    /// matching <c>filter</c> within the effective scope (F61.1, F61.2). Body:
    /// <c>{ "filter": {...}, "direction": "up" | "down" }</c> (case-insensitive direction).
    /// Missing filter or invalid direction → 400, nothing written. Success → 200 <c>{ updated }</c>.
    /// </summary>
    [HttpPost("media/bulk/vote")]
    [Consumes("application/json")]
    public async Task<IActionResult> BulkVote(
        [FromBody] BulkVoteRequest request,
        CancellationToken ct)
    {
        if (request.Filter is null)
            return MissingFilterProblem();

        if (!TryParseDirection(request.Direction, out var direction))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Invalid direction.",
                Detail = "direction must be \"up\" or \"down\".",
            });
        }

        // Named library-id overrides the station rotation scope (F23.3): the named library
        // becomes the effective scope. An unnamed filter stays bounded by the station scope
        // (F61.3 — bulk ranking is scope-bounded, unlike the per-row RatingController).
        var (scope, _) = EffectiveScope.Resolve(scopeProvider.Current, request.Filter.LibraryId);
        var mediaQuery = ToMediaQuery(request.Filter);

        var updated = await rating.BulkVoteAsync(mediaQuery, direction, scope, ct);

        logger.LogInformation(
            "BulkVote direction={Direction} filter={@Filter} updated={Updated}",
            direction, request.Filter, updated);

        return Ok(new { updated });
    }

    /// <summary>
    /// POST /api/media/bulk/never-play — idempotently set the never-play flag to <c>neverPlay</c>
    /// on every row matching <c>filter</c> within the effective scope (F61.1, F61.2). Body:
    /// <c>{ "filter": {...}, "neverPlay": bool }</c>. Missing filter → 400, nothing written.
    /// Success → 200 <c>{ updated }</c>.
    /// </summary>
    [HttpPost("media/bulk/never-play")]
    [Consumes("application/json")]
    public async Task<IActionResult> BulkSetNeverPlay(
        [FromBody] BulkNeverPlayRequest request,
        CancellationToken ct)
    {
        if (request.Filter is null)
            return MissingFilterProblem();

        // Same effective-scope contract as BulkVote (F61.3).
        var (scope, _) = EffectiveScope.Resolve(scopeProvider.Current, request.Filter.LibraryId);
        var mediaQuery = ToMediaQuery(request.Filter);

        var updated = await rating.BulkSetNeverPlayAsync(mediaQuery, request.NeverPlay, scope, ct);

        logger.LogInformation(
            "BulkSetNeverPlay neverPlay={NeverPlay} filter={@Filter} updated={Updated}",
            request.NeverPlay, request.Filter, updated);

        return Ok(new { updated });
    }

    /// <summary>
    /// Maps the bulk rating filter to the shared <see cref="MediaQuery"/> so both endpoints reuse
    /// the exact WHERE builder every other admin filter path uses (F61.1's "one shared WHERE
    /// builder"). <see cref="MediaQuery.NeverPlay"/> and <see cref="MediaQuery.Eligible"/> are left
    /// at their default (null/no-filter) — <see cref="BulkRatingFilter"/> structurally carries
    /// neither field, so a bulk rating sweep can never diverge from the browse preview the way a
    /// smuggled <c>neverPlay</c> filter value would (F33.10 browse-only).
    /// </summary>
    static MediaQuery ToMediaQuery(BulkRatingFilter filter) => new(
        State:       filter.State,
        Artist:      filter.Artist,
        Genre:       filter.Genre,
        LibraryId:   filter.LibraryId,
        Q:           filter.Q,
        ArtistExact: filter.ArtistExact,
        AlbumExact:  filter.AlbumExact,
        GenresExact: filter.GenresExact);

    static BadRequestObjectResult MissingFilterProblem() => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "filter is required.",
        Detail = "The request body must include a filter object.",
    });

    /// <summary>
    /// Case-insensitive match against the only two valid direction values. Duplicated from
    /// <see cref="RatingController"/>'s identical private helper (rather than extracted to a
    /// shared type) so that controller's file stays byte-for-byte untouched (STORY-158 hard rule,
    /// F33.5 exemption stands).
    /// </summary>
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
