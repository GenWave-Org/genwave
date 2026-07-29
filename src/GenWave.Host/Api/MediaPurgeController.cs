using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Api;

/// <summary>
/// Explicit operator purge for long-unavailable media rows (gh-#113): the "reconcile on a later
/// policy" PRD §5.1 always deferred, made an explicit, operator-initiated action — never a
/// background job. Its own controller (the <see cref="BulkRatingController"/> /
/// <see cref="ReenrichController"/> precedent for a distinct write concern) so
/// <see cref="MediaController"/> gains no new constructor dependency.
///
/// <see cref="AuthorizationPolicies.AdminOnly"/> rather than the Curation plane every other bulk
/// media write uses: a hard-delete is not curation — it is irreversible library administration,
/// and under a future RBAC split it must stay with session-level admin trust, not with whoever
/// can shape rotation.
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class MediaPurgeController(
    IMediaPurge purge,
    ILogger<MediaPurgeController> logger) : ControllerBase
{
    /// <summary>The age window applied when the request names none — one week of grace.</summary>
    internal const int DefaultOlderThanDays = 7;

    /// <summary>
    /// POST /api/media/purge-unavailable — hard-delete every row unavailable longer than
    /// <c>olderThanDays</c> days (default 7, minimum 1), including dependent rows
    /// (<c>library.media_rating</c> cascades). Body: <c>{ "olderThanDays"?: int, "dryRun"?: bool }</c>.
    ///
    /// Security contract:
    ///   • Requires cookie auth under the <see cref="AuthorizationPolicies.AdminOnly"/> policy.
    ///   • Requires Content-Type: application/json — rejects other types with 415 (CSRF guard).
    ///   • <c>olderThanDays</c> &lt; 1 → 400, nothing counted or deleted.
    ///   • Tripwire: candidates exceeding half the library → 409 ProblemDetails naming the counts,
    ///     nothing deleted (the mount-outage guard — a shrunk mount flips most of the catalog
    ///     unavailable, and that catalog must survive the mount coming back). Fires on dry runs
    ///     too, so the UI's count fetch already surfaces the refusal.
    ///
    /// Success:
    ///   • <c>dryRun: true</c>  → 200 <c>{ wouldDelete: n }</c> — nothing deleted.
    ///   • otherwise            → 200 <c>{ deleted: n }</c>.
    /// </summary>
    [HttpPost("media/purge-unavailable")]
    [Consumes("application/json")]
    public async Task<IActionResult> PurgeUnavailable(
        [FromBody] PurgeUnavailableRequest request,
        CancellationToken ct)
    {
        var olderThanDays = request.OlderThanDays ?? DefaultOlderThanDays;
        if (olderThanDays < 1)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Invalid olderThanDays.",
                Detail = $"olderThanDays must be at least 1; got {olderThanDays}.",
            });
        }

        var outcome = await purge.PurgeUnavailableAsync(olderThanDays, request.DryRun, ct);

        if (outcome.TripwireTripped)
        {
            logger.LogWarning(
                "PurgeUnavailable refused: {Candidates} of {LibraryTotal} rows would be deleted (over half the library) — possible mount outage",
                outcome.Candidates, outcome.LibraryTotal);

            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title  = "Purge refused.",
                Detail = $"{outcome.Candidates} of {outcome.LibraryTotal} tracks would be deleted — more than half " +
                         "the library. That pattern usually means the media mount is down or was remounted empty, " +
                         "not that the tracks are gone for good. Check the mount (and let a scan see it) before purging.",
            });
        }

        if (request.DryRun)
            return Ok(new { wouldDelete = outcome.Candidates });

        logger.LogInformation(
            "PurgeUnavailable deleted {Deleted} rows unavailable longer than {OlderThanDays} days ({LibraryTotal} rows in library before purge)",
            outcome.Deleted, olderThanDays, outcome.LibraryTotal);

        return Ok(new { deleted = outcome.Deleted });
    }
}
