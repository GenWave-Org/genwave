namespace GenWave.Tts;

using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// The single hand-off point from booth-bound copy to the TTS renderer (SPEC F68.1). Every caller
/// of <see cref="ITtsSynthesizer"/> — <see cref="TtsSegmentSource"/> (patter: LLM copy, template
/// copy), <see cref="SafeSegmentAuthor"/> (authored/safe-loop segments), and the admin preview
/// endpoint — resolves this decorator, so <see cref="SpeechText.Normalize"/> runs exactly once,
/// right here, immediately before the real synthesis call. No caller performs its own pre-TTS
/// cleanup; this IS "the hand-off to the TTS renderer" F68.1 requires, not a caller-side concern.
///
/// Decorates the concrete synthesizer (mirrors <see cref="CachedVoiceLister"/>'s decorator shape
/// one seam over on <see cref="ITtsVoiceLister"/>) rather than being folded into
/// <see cref="KokoroTtsSynthesizer"/> itself, so the HTTP client stays a pure Kokoro adapter and
/// this stays a pure text-hand-off concern — two reasons to change, two classes.
///
/// Also implements <see cref="ISpeechNormalizationPreview"/> (SPEC F68.6, STORY-186 AC2): the admin
/// preview endpoint resolves this SAME registered instance (see <see cref="LlmCopyWriter"/>'s
/// analogous two-seam registration) so a preview can never drift from what a real render produces,
/// with no TTS render and no observability side effects.
///
/// Fired-rule observability (SPEC F68.7, STORY-186 AC3): every real render — never a preview —
/// logs one debug line and increments <see cref="CorrectionsFiredStats"/> per rule that actually
/// changed the text, read back by <c>GET /api/tts/corrections-stats</c>. <see cref="SpeechCorrectionSet"/>
/// itself stays pure (it only reports which rules fired via an out parameter); this decorator is
/// where that report becomes a log line and a counter.
/// </summary>
public sealed class NormalizingTtsSynthesizer(
    ITtsSynthesizer inner,
    SpeechCorrectionProvider corrections,
    CorrectionsFiredStats firedStats,
    ILogger<NormalizingTtsSynthesizer> logger) : ITtsSynthesizer, ISpeechNormalizationPreview
{
    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct) =>
        SynthesizeAsync(new TtsRenderContext(text, voice, Kind: null), ct);

    /// <summary>
    /// Kind-aware overload (SPEC F70.3, STORY-191): normalizes exactly as the plain two-arg overload
    /// always has, but relays <see cref="TtsRenderContext.Kind"/> unchanged to <paramref
    /// name="inner"/> (typically <see cref="FallbackTtsSynthesizer"/>) so its per-kind engine map can
    /// see which speech kind is rendering. This decorator never inspects <c>Kind</c> itself —
    /// normalization and correction firing are kind-agnostic — it only relays it downstream.
    /// </summary>
    public Task<string> SynthesizeAsync(TtsRenderContext context, CancellationToken ct)
    {
        var snapshot = corrections.Current;
        var normalized = RunNormalize(context.Text, snapshot);
        ReportFiredCorrections(context.Text, context.Voice, snapshot);
        return inner.SynthesizeAsync(context with { Text = normalized }, ct);
    }

    /// <inheritdoc/>
    public string Preview(string text) => RunNormalize(text, corrections.Current);

    /// <summary>
    /// The one call to <see cref="SpeechText.Normalize"/> in this codebase (SPEC F68.1) — both
    /// <see cref="SynthesizeAsync"/> and <see cref="Preview"/> funnel through here so a preview can
    /// never drift from what a real render produces.
    /// </summary>
    static string RunNormalize(string text, SpeechCorrectionSet snapshot) => SpeechText.Normalize(text, snapshot);

    /// <summary>
    /// Determines which rules fired for THIS render — via <see cref="SpeechText.PrepareForCorrections"/>
    /// and <see cref="SpeechCorrectionSet.Apply"/>'s out parameter, the same pre-corrections text
    /// <see cref="RunNormalize"/> itself matches against — and logs/counts each one. Never called
    /// from <see cref="Preview"/>: a preview is not a broadcast, so it must not pollute the
    /// operator-facing fired counters or the debug log with trial runs.
    /// </summary>
    void ReportFiredCorrections(string text, string voice, SpeechCorrectionSet snapshot)
    {
        var prepared = SpeechText.PrepareForCorrections(text);
        snapshot.Apply(prepared, out var firedFroms);

        foreach (var from in firedFroms)
        {
            firedStats.RecordFired(from);
            // Operator-authored rule text and voice id are newline-stripped so they can't
            // forge additional log entries (CodeQL cs/log-forging).
            logger.LogDebug(
                "TTS correction fired: from={CorrectionFrom} voice={Voice}",
                from.ReplaceLineEndings(" "), voice.ReplaceLineEndings(" "));
        }
    }
}
