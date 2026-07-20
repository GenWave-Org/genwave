namespace GenWave.Tts;

using GenWave.Core.Abstractions;

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
/// </summary>
public sealed class NormalizingTtsSynthesizer(
    ITtsSynthesizer inner,
    SpeechCorrectionProvider corrections) : ITtsSynthesizer
{
    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        var normalized = SpeechText.Normalize(text, corrections.Current);
        return inner.SynthesizeAsync(normalized, voice, ct);
    }
}
