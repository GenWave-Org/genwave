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
/// The persona CARD's own corrections — card-only, NOT the merged set, and empty for a station
/// snapshot. Unlike <see cref="Rules"/>, which the source merges at resolve time, the station side
/// is re-merged downstream: <c>GenWave.Tts.NormalizingTtsSynthesizer</c> merges these with the
/// operator's live station corrections through the same <c>BuildMerged</c> seam,
/// CARD-over-station, exactly as the ambient path does (SPEC F97.4, PLAN T526).
/// A reader who took this for the merged set would conclude the station corrections are applied
/// twice; they are not.
/// </param>
/// <param name="ContentHash">
/// A deterministic digest over the same inputs the ambient <c>SpeechCorrectionProvider</c> /
/// <c>PronunciationRuleProvider</c> / <c>ActivePersonaCorrectionsCache</c> caches hash today (SPEC
/// F189.3) — folded into the TTS cache key so a render under this snapshot invalidates on exactly
/// the same changes a render under the ambient path would. It does NOT make the two paths share a
/// cache ENTRY: the snapshot arm hashes its own formula, so its key space is disjoint from the
/// ambient arm's (<c>GenWave.Tts.TtsRenderKey</c>). The first snapshot-driven render of an
/// evergreen clip re-synthesizes rather than reusing the ambient file.
/// </param>
public sealed record SpeakerSnapshot(
    long? PersonaId,
    string? PersonaName,
    string Voice,
    double Pace,
    IReadOnlyList<PronunciationRule> Rules,
    IReadOnlyList<SpeechCorrection> Corrections,
    string ContentHash)
{
    /// <summary>
    /// True when this speaker has no voice for the active TTS engine (SPEC F219.9, PLAN T753): its talk is
    /// dropped with a <c>voice-missing</c> booth row, never rendered with another engine's voice id.
    /// <see cref="Voice"/> is empty then.
    /// </summary>
    public bool VoiceMissing { get; init; }

    /// <summary>
    /// True when <see cref="Voice"/> came from the persona's per-engine map because the active engine is NOT the
    /// engine of the card's own voice, so the persona row's flat voice does not apply and the planner stamps this
    /// voice instead (SPEC F219.11, PLAN T753). False when the active engine owns the card voice: the validated row
    /// voice stays authoritative. Needed because <see cref="Voice"/> stays the card's voice (published contract)
    /// and two engines may share a voice id, so the planner cannot tell the cases apart from the strings.
    /// </summary>
    public bool VoiceOverridesRow { get; init; }
}
