using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Api;

/// <summary>
/// POST /api/safe-segments — operator self-serve authoring of a branded safe-loop segment
/// (SPEC F27.3, STORY-079). Synchronously renders through <see cref="ISafeSegmentAuthor"/> — the
/// same all-or-nothing pipeline the boot seed (P7) uses — and returns the created row in the same
/// shape as <c>GET /api/media/{id}</c>.
///
/// Security contract (F18.7 posture, same as the shipped writes in <see cref="MediaController"/>
/// and <see cref="LibrariesController"/>):
/// <list type="bullet">
///   <item>Requires cookie auth (deny-by-default fallback policy when Admin:Password is set).</item>
///   <item>Requires <c>Content-Type: application/json</c> — <c>[Consumes]</c> rejects other types with 415.</item>
/// </list>
///
/// Validation runs BEFORE any render (F27.3): blank <c>text</c>, an unknown <c>libraryId</c>, or an
/// unknown <c>bedMediaId</c> all return 400 ProblemDetails with nothing rendered or persisted. A
/// <c>bedMediaId</c> is never trusted as a raw path — it is resolved to its catalog row so the mixer
/// receives a <see cref="BedSpec"/> built from the row's own path and cue points.
///
/// A synthesis/mix/measurement/insert failure reported by <see cref="ISafeSegmentAuthor"/>, or the
/// render exceeding <c>Tts:RenderBudgetSeconds</c>, returns 502 ProblemDetails with no internals
/// leaked (the underlying reason/detail is logged, never echoed to the caller).
/// </summary>
[ApiController]
[Route("api/safe-segments")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class SafeSegmentsController(
    ISafeSegmentAuthor author,
    ILibraryRepository libraryRepository,
    IAdminMediaLookup adminLookup,
    IOptionsMonitor<StationOptions> stationMonitor,
    IOptionsMonitor<TtsOptions> ttsMonitor,
    ILogger<SafeSegmentsController> logger) : ControllerBase
{
    const string GenericFailureDetail =
        "The safe-segment could not be generated. Check the server logs for details.";

    /// <summary>
    /// POST /api/safe-segments — see the class remarks for the full contract.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create(
        [FromBody] SafeSegmentCreateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Validation error.",
                Detail = "text must not be blank or whitespace.",
            });
        }

        if (request.LibraryId is null)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Validation error.",
                Detail = "libraryId is required.",
            });
        }

        var libraryId  = request.LibraryId.Value;
        var libraries  = await libraryRepository.GetByIdsAsync([libraryId], ct);
        if (libraries.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Unknown library.",
                Detail = $"No library with id {libraryId} exists.",
            });
        }

        var (bed, bedError) = await ResolveBedAsync(request.BedMediaId, ct);
        if (bedError is not null)
            return bedError;

        var station = stationMonitor.CurrentValue;
        var safe    = station.Safe;

        var authorRequest = new SafeSegmentRequest(
            Text: request.Text,
            LibraryId: libraryId,
            StationName: station.Name,
            DefaultVoice: station.Voice,
            AuthoredRoot: safe.AuthoredRoot,
            BedDuckDb: safe.BedDuckDb,
            BedPadSeconds: safe.BedPadSeconds,
            Title: request.Title,
            Voice: request.Voice,
            Bed: bed);

        var budget = TimeSpan.FromSeconds(ttsMonitor.CurrentValue.RenderBudgetSeconds);
        using var boundedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        boundedCts.CancelAfter(budget);

        SafeSegmentAuthorResult result;
        try
        {
            result = await author.AuthorAsync(authorRequest, boundedCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // boundedCts fired on the budget timeout, not the caller disconnecting — a bounded
            // failure the caller should see, not an unhandled exception (F27.3).
            logger.LogWarning(
                "Safe-segment render exceeded Tts:RenderBudgetSeconds={BudgetSeconds}s",
                budget.TotalSeconds);
            return BadGateway();
        }

        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Safe-segment authoring failed reason={FailureReason} detail={FailureDetail}",
                result.FailureReason, result.FailureDetail);
            return BadGateway();
        }

        // Re-fetch through the same admin lookup GET /api/media/{id} uses so the response carries
        // the identical projection (and xmin-derived ETag) with no bespoke shape (F27.3).
        var created = await adminLookup.GetByIdWithLibraryAsync(result.MediaId, ct);
        if (created is null)
        {
            logger.LogError(
                "Safe-segment insert reported success (id={MediaId}) but the row could not be re-read.",
                result.MediaId);
            return BadGateway();
        }

        var row = created.Value.Row;
        Response.Headers.ETag = $"W/\"{row.Version}\"";
        return Created($"/api/media/{row.MediaId}", row);
    }

    /// <summary>
    /// Resolves an optional <c>bedMediaId</c> to a <see cref="BedSpec"/> built from the referenced
    /// row's own path and cue points — never a caller-supplied path (F27.3, the P4 reviewer's
    /// forward note on path safety). Returns <c>(null, null)</c> when <paramref name="bedMediaId"/>
    /// is absent, <c>(spec, null)</c> on success, or <c>(null, error)</c> when the id is unknown.
    /// </summary>
    async Task<(BedSpec? Bed, IActionResult? Error)> ResolveBedAsync(long? bedMediaId, CancellationToken ct)
    {
        if (bedMediaId is null)
            return (null, null);

        var found = await adminLookup.GetByIdWithLibraryAsync(bedMediaId.Value, ct);
        if (found is null)
        {
            return (null, BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Unknown bed media.",
                Detail = $"No media row with id {bedMediaId.Value} exists.",
            }));
        }

        var row = found.Value.Row;
        var (cueIn, cueOut) = ResolveBedCue(bedMediaId.Value, row.CueInSec, row.CueOutSec);
        return (new BedSpec(row.Locator, cueIn, cueOut), null);
    }

    /// <summary>
    /// Mirrors <c>MediaRow.ResolveCue</c>'s inverted/asymmetric-cue discipline (MediaLibrary project)
    /// so a malformed bed row degrades to no-cue with a WARN instead of throwing out of
    /// <see cref="BedSpec"/>'s constructor guard (both non-null and in order → kept; both null →
    /// no cue; asymmetric or inverted → no cue, logged).
    /// </summary>
    (double? CueIn, double? CueOut) ResolveBedCue(long bedMediaId, double? cueInSec, double? cueOutSec)
    {
        if (cueInSec.HasValue && cueOutSec.HasValue)
        {
            if (cueInSec.Value >= cueOutSec.Value)
            {
                logger.LogWarning(
                    "Bed media {BedMediaId} has inverted cue columns (in={CueIn}, out={CueOut}) — treating as no cue",
                    bedMediaId, cueInSec.Value, cueOutSec.Value);
                return (null, null);
            }
            return (cueInSec, cueOutSec);
        }

        if (!cueInSec.HasValue && !cueOutSec.HasValue)
            return (null, null);

        logger.LogWarning(
            "Bed media {BedMediaId} has asymmetric cue columns — treating as no cue", bedMediaId);
        return (null, null);
    }

    ObjectResult BadGateway() =>
        StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title  = "Safe-segment generation failed.",
            Detail = GenericFailureDetail,
        });
}
