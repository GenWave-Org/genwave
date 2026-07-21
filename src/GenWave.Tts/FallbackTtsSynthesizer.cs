namespace GenWave.Tts;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

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
/// "empty endpoint = zero behavior change vs today"). This short-circuit runs BEFORE any per-kind
/// lookup below, so an operator cannot map a kind to Piper while Piper itself is undeployed — there
/// is no engine to route to.
///
/// Per-kind override interplay (SPEC F70.3, STORY-191): an optional <c>Tts:EngineByKind</c> map
/// (<see cref="TtsEngineOverrideMap"/>, read via <see cref="TtsEngineByKindProvider"/>) lets an
/// operator PIN a speech kind to Piper regardless of Kokoro's cached health. That pin pre-empts the
/// health-based routing below ONLY in the forward direction — it decides which engine is tried
/// FIRST, nothing more. Resilience stays exactly as symmetric as the unmapped path: if the pinned
/// Piper render throws, this decorator still retries once on Kokoro, and if THAT also throws the
/// exception propagates unchanged (the segment skips loudly, same as F70.1's both-down case). A
/// kind with no map entry — or the whole map when <c>Tts:EngineByKind</c> is empty/unset — takes the
/// untouched F70.1 health-based path below, so an empty map is byte-identical to pre-feature
/// routing (F70.3's own AC3). Mapping a kind explicitly to <c>"kokoro"</c> is legal but is not a
/// distinct code path: it just re-enters the same health-based routing every unmapped kind already
/// gets, since Kokoro is already tried first there.
/// </summary>
public sealed class FallbackTtsSynthesizer(
    ITtsSynthesizer primary,
    ITtsSynthesizer fallback,
    IDependencyHealth health,
    IOptionsMonitor<TtsFallbackOptions> fallbackOptions,
    ILogger<FallbackTtsSynthesizer> logger,
    TtsEngineByKindProvider? engineOverrides = null) : ITtsSynthesizer
{
    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct) =>
        SynthesizeAsync(new TtsRenderContext(text, voice, Kind: null), ct);

    /// <summary>
    /// Kind-aware overload (SPEC F70.3, STORY-191) — see the class remarks for the full per-kind
    /// interplay with F70.1's health-based routing.
    /// </summary>
    public async Task<string> SynthesizeAsync(TtsRenderContext context, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(fallbackOptions.CurrentValue.Endpoint))
        {
            // No Piper sidecar configured — identical to pre-T34 behavior (F70.1). The per-kind map
            // is moot when there is no second engine to route to.
            return await primary.SynthesizeAsync(context.Text, context.Voice, ct);
        }

        var mappedEngine = context.Kind is { } kind
            ? (engineOverrides?.Current ?? TtsEngineOverrideMap.Empty).Resolve(kind)
            : null;

        if (mappedEngine == DependencyNames.Piper)
        {
            // Forward-direction pre-emption (F70.3): go straight to Piper without consulting the
            // cached Kokoro verdict. Resilience stays symmetric — a Piper failure here still falls
            // through to Kokoro, the mirror image of the primary-throws-falls-to-fallback path below.
            try
            {
                return await fallback.SynthesizeAsync(context.Text, context.Voice, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Piper render failed for kind {Kind} mapped by Tts:EngineByKind; retrying once via Kokoro",
                    context.Kind);
                return await primary.SynthesizeAsync(context.Text, context.Voice, ct);
            }
        }

        // Unmapped kind, a "kokoro" pin, or no Kind at all — the untouched F70.1 health-based
        // routing (STORY-191 AC2/AC3).
        var verdict = health.GetVerdict(DependencyNames.Kokoro);
        if (verdict is { Healthy: false })
        {
            logger.LogWarning(
                "Kokoro cached verdict is unhealthy ({Reason}); routing render straight to Piper fallback",
                verdict.Reason);
            return await fallback.SynthesizeAsync(context.Text, context.Voice, ct);
        }

        try
        {
            return await primary.SynthesizeAsync(context.Text, context.Voice, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kokoro render failed; retrying once via Piper fallback");
            return await fallback.SynthesizeAsync(context.Text, context.Voice, ct);
        }
    }
}
