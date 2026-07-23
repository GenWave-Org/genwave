using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Tts;

namespace GenWave.Host.Api;

/// <summary>
/// Admin-only endpoints for the corrections editor (SPEC F68.6–F68.7, STORY-186): a pure
/// spoken-form preview (no TTS render) and per-rule fired counters. Both read the SAME registered
/// <see cref="NormalizingTtsSynthesizer"/> instance / <see cref="CorrectionsFiredStats"/> singleton
/// the real render path uses — never a second, drifted normalization or a separate counter set.
/// </summary>
[ApiController]
[Route("api/tts")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class TtsCorrectionsController(
    ISpeechNormalizationPreview preview,
    CorrectionsFiredStats stats) : ControllerBase
{
    /// <summary>
    /// POST /api/tts/normalize-preview — runs <c>text</c> through the REAL
    /// <see cref="SpeechText.Normalize"/> chokepoint (via <see cref="ISpeechNormalizationPreview"/>)
    /// against the CURRENT corrections snapshot: no TTS render, no fired-rule counters (a preview
    /// is not a broadcast), cheap enough for an operator to run on demand (SPEC F68.6, STORY-186
    /// AC2 — "the spoken form shown matches SpeechText.Normalize output" holds by construction,
    /// since this endpoint IS that call).
    /// </summary>
    [HttpPost("normalize-preview")]
    [Consumes("application/json")]
    public IActionResult NormalizePreview([FromBody] NormalizePreviewRequest request)
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

        var spoken = preview.Preview(request.Text);
        return Ok(new NormalizePreviewResponse(spoken));
    }

    /// <summary>
    /// GET /api/tts/corrections-stats — per-rule fired counts since process start (SPEC F68.7,
    /// STORY-186 AC3), so an operator can confirm a saved rule is actually firing on real renders.
    /// A rule that has never fired is simply absent from the response, never a zero-count row.
    /// </summary>
    [HttpGet("corrections-stats")]
    public IActionResult CorrectionsStats()
    {
        var rows = stats.Snapshot()
            .Select(entry => new CorrectionStatDto(entry.From, entry.Fired))
            .ToList();

        return Ok(rows);
    }
}
