using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Tts;

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
    IVoiceEngineRegistry engines,
    SpeechEngineClient speechClient,
    IOptionsMonitor<TtsOptions> ttsOptions,
    ILogger<VoicesController> logger) : ControllerBase
{
    /// <summary>
    /// GET /api/voices — 200 with a JSON array of voice id strings on success; 502 ProblemDetails
    /// when the backend is unreachable and the cache is cold/expired (AC3).
    ///
    /// <para>
    /// <c>?engine=&lt;name&gt;</c> (SPEC F219.10) lists that registry engine's voices instead, for the DJ page's
    /// picker per engine: 400 for a name the registry does not hold, the same 502 when it is unreachable. An
    /// engine that cannot choose a voice per request (piper, <see cref="VoiceEngineProfile.PicksVoice"/> false)
    /// lists none. <c>kokoro</c> reads the same cached list as the bare call. Omitted (or blank) is today's call.
    /// </para>
    /// </summary>
    [HttpGet("voices")]
    public async Task<IActionResult> Get([FromQuery] string? engine, CancellationToken ct)
    {
        VoiceEngine? named = null;
        if (!string.IsNullOrWhiteSpace(engine))
        {
            named = engines.Get(engine);
            if (named is null)
                return BadRequest(VoiceEngineProblems.Unknown());
        }

        try
        {
            return Ok(await ListAsync(named, ct));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Voices listing unreachable");
            return BadGateway();
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // The HttpClient's own request timeout fired — not the caller disconnecting.
            logger.LogWarning(ex, "Voices listing timed out");
            return BadGateway();
        }
    }

    Task<IReadOnlyList<string>> ListAsync(VoiceEngine? named, CancellationToken ct)
    {
        if (named is null || string.Equals(named.Name, VoiceEngineProfiles.KokoroName, StringComparison.OrdinalIgnoreCase))
            return voiceLister.ListVoicesAsync(ct);

        return named.Profile.PicksVoice
            ? speechClient.ListVoicesAsync(named, ct)
            : Task.FromResult<IReadOnlyList<string>>([]);
    }

    /// <summary>
    /// GET /api/voices/engines — the engines in the registry plus which one <c>Tts:Engine</c> names
    /// right now (SPEC F219.2), for the Settings "Voice engine" control. Read live, so a just-saved
    /// choice shows on the next call.
    /// </summary>
    [HttpGet("voices/engines")]
    public ActionResult<VoiceEnginesResponse> GetEngines() =>
        new VoiceEnginesResponse(
            ttsOptions.CurrentValue.Engine,
            [.. engines.All.Select(e => new VoiceEngineSummary(
                e.Name, e.Profile.Name, EngineRenderBudget.For(e, ttsOptions.CurrentValue), e.RenderBudgetSeconds))]);

    ObjectResult BadGateway() =>
        StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title  = "Voices listing unavailable.",
            Detail = "The TTS backend could not be reached. Try again shortly.",
        });
}
