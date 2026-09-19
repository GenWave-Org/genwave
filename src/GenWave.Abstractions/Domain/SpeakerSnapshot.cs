namespace GenWave.Core.Domain;

/// <summary>
/// The resolved voice a TTS render should use, captured once and carried on the request rather
/// than re-read from the ambient caches at render time (SPEC F189.1/F189.2, gh-#772). Produced by
/// <c>ISpeakerSnapshotSource</c> (a <c>GenWave.Core</c> seam implemented in <c>GenWave.Tts</c>):
/// <see cref="PersonaId"/> null means the station voice itself — no persona is on air — in which
/// case <see cref="PersonaName"/> is also null; a non-null <see cref="PersonaId"/> names the
/// persona whose card was read.
/// </summary>
/// <param name="PersonaId">
/// The persona this snapshot was resolved for, or <see langword="null"/> for the plain station
/// voice.
/// </param>
/// <param name="PersonaName">
/// The persona's display name, or <see langword="null"/> exactly when <see cref="PersonaId"/> is.
/// </param>
/// <param name="Voice">TTS voice identifier passed through to the synthesizer.</param>
/// <param name="Pace">The already-clamped speech pace this speaker renders at.</param>
/// <param name="Rules">
/// The merged pronunciation rules in effect for this speaker (station rules, or station rules
/// merged under the persona's own, per the same rule-over-correction precedence the ambient path
/// applies today).
/// </param>
/// <param name="Corrections">
/// The merged operator/persona corrections in effect for this speaker, same precedence as
/// <see cref="Rules"/>.
/// </param>
/// <param name="ContentHash">
/// A deterministic digest over the same inputs the ambient <c>SpeechCorrectionProvider</c> /
/// <c>PronunciationRuleProvider</c> / <c>ActivePersonaCorrectionsCache</c> caches hash today (SPEC
/// F189.3) — folded into the TTS cache key so a render under this snapshot invalidates exactly
/// when a render under the ambient path would.
/// </param>
public sealed record SpeakerSnapshot(
    long? PersonaId,
    string? PersonaName,
    string Voice,
    double Pace,
    IReadOnlyList<PronunciationRule> Rules,
    IReadOnlyList<SpeechCorrection> Corrections,
    string ContentHash);
