namespace GenWave.Tts;

/// <summary>
/// Text-only preview of the TTS normalization chokepoint (SPEC F68.6, STORY-186 AC2) — shows an
/// operator what <see cref="SpeechText.Normalize"/> would produce for arbitrary sample text against
/// the CURRENT corrections snapshot, with no TTS render and no fired-rule observability (a preview
/// is not a broadcast; see <see cref="NormalizingTtsSynthesizer"/>'s remarks). Implemented by
/// <see cref="NormalizingTtsSynthesizer"/> alongside <see cref="GenWave.Core.Abstractions.ITtsSynthesizer"/>
/// — the same "one concrete registration, two seams" shape <see cref="LlmCopyWriter"/> already uses
/// for <see cref="ISegmentCopyWriter"/>/<see cref="IPersonaPreviewWriter"/> — so the admin preview
/// endpoint (<c>POST /api/tts/normalize-preview</c>) exercises the EXACT SAME normalization the
/// render path uses, never a second, drifted pipeline.
/// </summary>
public interface ISpeechNormalizationPreview
{
    /// <summary>Runs <paramref name="text"/> through the real normalization pipeline against the
    /// current corrections snapshot. Pure: no TTS render, no counters, no logging.</summary>
    string Preview(string text);
}
