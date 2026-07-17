using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Api;

/// <summary>
/// POST /api/tts/preview — synchronous <c>audio/wav</c> preview of arbitrary text (SPEC F35.6,
/// STORY-123). Calls <see cref="ITtsSynthesizer"/> — the same production Kokoro client patter uses
/// — directly, with no <c>TtsSegmentSource</c> in front of it: no loudness/cue measurement, no
/// <c>MediaItem</c>, no catalog row, no engine annotation. Bounded by <c>Tts:RenderBudgetSeconds</c>,
/// mirroring <c>SafeSegmentsController</c>'s render-budget/502 shape.
///
/// PERSISTENCE (SPEC F35.6 "not persisted"): <see cref="ITtsSynthesizer.SynthesizeAsync"/> writes
/// its result to a content-addressed file under <c>Tts:CacheRoot</c> as an unavoidable side effect
/// of synthesizing — that write is the synthesizer's own production contract (the same file
/// <c>TtsSegmentSource</c> would move into the station's forever-cache on a real render), not
/// something a caller can suppress without a second synth overload. This endpoint reads the bytes
/// into memory for the response and then deletes the file it just caused to be written
/// (best-effort — a failed delete only leaves an orphan; that path's cache key is the synthesizer's
/// own (text,voice) hash, not <c>TtsSegmentSource</c>'s station-scoped hash, so no selection path
/// ever looks there). Nothing is measured, cued, wrapped in a <c>MediaItem</c>, or written to
/// <c>library.media</c> — there is no reachable path from this call into rotation.
/// </summary>
[ApiController]
[Route("api/tts")]
public sealed class TtsPreviewController(
    ITtsSynthesizer synthesizer,
    IOptionsMonitor<StationOptions> stationMonitor,
    IOptionsMonitor<TtsOptions> ttsMonitor,
    ILogger<TtsPreviewController> logger) : ControllerBase
{
    /// <summary>See the class remarks for the full contract.</summary>
    [HttpPost("preview")]
    [Consumes("application/json")]
    public async Task<IActionResult> Preview([FromBody] TtsPreviewRequest request, CancellationToken ct)
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

        var voice  = string.IsNullOrWhiteSpace(request.Voice) ? stationMonitor.CurrentValue.Voice : request.Voice;
        var budget = TimeSpan.FromSeconds(ttsMonitor.CurrentValue.RenderBudgetSeconds);

        using var boundedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        boundedCts.CancelAfter(budget);

        string path;
        try
        {
            path = await synthesizer.SynthesizeAsync(request.Text, voice, boundedCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // boundedCts fired on the render budget, not the caller disconnecting.
            logger.LogWarning(
                "TTS preview synthesis exceeded Tts:RenderBudgetSeconds={BudgetSeconds}s", budget.TotalSeconds);
            return BadGateway();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TTS preview synthesis failed");
            return BadGateway();
        }

        byte[] bytes;
        try
        {
            bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
        }
        finally
        {
            DeletePreviewArtifact(path);
        }

        return File(bytes, "audio/wav");
    }

    /// <summary>
    /// Best-effort cleanup of the file <see cref="ITtsSynthesizer.SynthesizeAsync"/> just wrote — see
    /// the class remarks for why this is the smallest honest way to keep a preview from
    /// accumulating an artifact that looks like content.
    /// </summary>
    void DeletePreviewArtifact(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not delete TTS preview artifact {Path}; it will be orphaned in the cache", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Could not delete TTS preview artifact {Path}; it will be orphaned in the cache", path);
        }
    }

    ObjectResult BadGateway() =>
        StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title  = "TTS preview generation failed.",
            Detail = "The preview audio could not be generated. Check the server logs for details.",
        });
}
