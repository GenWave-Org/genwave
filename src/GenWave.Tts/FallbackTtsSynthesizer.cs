namespace GenWave.Tts;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

/// <summary>
/// Routes each render to the primary (Kokoro) or fallback (Piper) engine (SPEC F70.1, F70.4,
/// STORY-190). Sits BELOW <see cref="NormalizingTtsSynthesizer"/> — that decorator wraps THIS one,
/// never the other way round — so <see cref="SpeechText.Normalize"/> runs exactly once, before
/// this decorator ever sees the text: both engines receive identical already-normalized copy, and
/// both flow through the exact same <see cref="TtsSegmentSource"/> measure/cue/cache pipeline one
/// seam up (F70.4) — nothing downstream of <see cref="ITtsSynthesizer"/> needs to know which engine
/// actually rendered a clip.
///
/// Routing rule (F70.1, F70.2): reads <see cref="IDependencyHealth"/>'s CACHED Kokoro verdict —
/// never probes here, so a render-time decision costs zero network round trips beyond whichever
/// synthesis call it makes (STORY-187 AC2, "no health check executes inside the render window").
/// A cached <c>unhealthy</c> verdict routes straight to Piper without even trying Kokoro; a
/// <c>healthy</c> verdict, or no verdict yet (the brief startup window before the first probe
/// cycle completes), tries Kokoro first and retries once on Piper if that throws. Both down (Piper
/// also throws, or was never reachable) rethrows whichever exception was actually last attempted —
/// this decorator adds no exception wrapping of its own — so <see cref="TtsSegmentSource"/>'s
/// existing render-ahead catch turns it into a loud <c>LogWarning</c> and a skipped segment; music
/// keeps playing (STORY-190 AC4).
///
/// An empty <see cref="TtsFallbackOptions.Endpoint"/> means Piper is not deployed — this decorator
/// is then a transparent pass-through to Kokoro: no health read, no retry, no second exception in
/// the log. A Kokoro failure propagates exactly as it did before this feature existed (F70.1,
/// "empty endpoint = zero behavior change vs today").
/// </summary>
public sealed class FallbackTtsSynthesizer(
    ITtsSynthesizer primary,
    ITtsSynthesizer fallback,
    IDependencyHealth health,
    IOptionsMonitor<TtsFallbackOptions> fallbackOptions,
    ILogger<FallbackTtsSynthesizer> logger) : ITtsSynthesizer
{
    public async Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(fallbackOptions.CurrentValue.Endpoint))
        {
            // No Piper sidecar configured — identical to pre-T34 behavior (F70.1).
            return await primary.SynthesizeAsync(text, voice, ct);
        }

        var verdict = health.GetVerdict(DependencyNames.Kokoro);
        if (verdict is { Healthy: false })
        {
            logger.LogWarning(
                "Kokoro cached verdict is unhealthy ({Reason}); routing render straight to Piper fallback",
                verdict.Reason);
            return await fallback.SynthesizeAsync(text, voice, ct);
        }

        try
        {
            return await primary.SynthesizeAsync(text, voice, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kokoro render failed; retrying once via Piper fallback");
            return await fallback.SynthesizeAsync(text, voice, ct);
        }
    }
}
