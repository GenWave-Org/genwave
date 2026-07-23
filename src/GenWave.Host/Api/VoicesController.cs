using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Api;

/// <summary>
/// GET /api/voices — cookie-auth proxy of Kokoro's voice listing over the <c>core</c> network
/// (SPEC F29.4, STORY-097), for the Safe content Voice dropdown (R3). Kokoro is never published
/// outside the compose network; the browser only ever talks to this endpoint.
///
/// The actual upstream call and the ~5 min in-memory TTL both live behind
/// <see cref="ITtsVoiceLister"/> (<c>KokoroVoiceLister</c> wrapped by <c>CachedVoiceLister</c>,
/// wired in Program.cs) — this controller's only job is translating a cache-miss upstream failure
/// into 502 ProblemDetails with no internal hostnames in the response (F15.7).
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Operator)]
public sealed class VoicesController(
    ITtsVoiceLister voiceLister,
    ILogger<VoicesController> logger) : ControllerBase
{
    /// <summary>
    /// GET /api/voices — 200 with a JSON array of voice id strings on success; 502 ProblemDetails
    /// when Kokoro is unreachable and the cache is cold/expired (AC3).
    /// </summary>
    [HttpGet("voices")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try
        {
            var voices = await voiceLister.ListVoicesAsync(ct);
            return Ok(voices);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Kokoro voices listing unreachable");
            return BadGateway();
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // The HttpClient's own request timeout fired — not the caller disconnecting.
            logger.LogWarning(ex, "Kokoro voices listing timed out");
            return BadGateway();
        }
    }

    ObjectResult BadGateway() =>
        StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title  = "Voices listing unavailable.",
            Detail = "The TTS backend could not be reached. Try again shortly.",
        });
}
