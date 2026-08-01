namespace GenWave.Core.Domain;

/// <summary>
/// One resolved pronunciation rule, shaped for <see cref="TtsRenderContext.Rules"/> (SPEC F97.1,
/// F97.6): <see cref="Pattern"/> is the literal, word-boundary-anchored phrase a rule must match;
/// <see cref="Word"/> is the token *within* that match to re-pronounce; <see cref="Ipa"/> is the
/// phoneme string a supporting engine renders in its place.
///
/// Mirrors <c>GenWave.Tts.PronunciationRule</c>'s identical <c>{Pattern, Word, Ipa}</c> shape by
/// deliberate instruction rather than by shared type — the same posture <see cref="PersonaCorrection"/>
/// already takes on <c>GenWave.Tts.SpeechCorrection</c> (SPEC F71.1): this project (the MIT contract
/// surface, zero dependencies) cannot reference <c>GenWave.Tts</c>, where the compiled,
/// regex-matched runtime type (<c>PronunciationRuleSet</c>) lives. A caller that needs the compiled
/// matcher (the Kokoro adapters, <c>GenWave.Tts</c>) converts this plain data into
/// <c>PronunciationRuleSet.Create</c>'s own input shape; this record carries nothing but the
/// resolved values themselves — no compiled state, no matching behavior.
///
/// <para>
/// <b>The precedent cited above has already drifted — cite it honestly, not as a clean success.</b>
/// <c>GenWave.Tts.SpeechCorrection</c> gained <c>WhenPrecededBy</c>/<c>WhenFollowedBy</c> at gh-#161;
/// <see cref="PersonaCorrection"/> never did. <c>ActivePersonaCorrectionsCache</c> converts every
/// card correction to a <c>SpeechCorrection</c> with both left null, so a persona card cannot
/// express a conditional correction a station rule can — lossy today, silently, because nothing
/// forced the mirror to track the field addition it was modeled on. This type must not repeat that:
/// any future field added to <c>GenWave.Tts.PronunciationRule</c> needs the same field added here in
/// the SAME change, or the ONE conversion seam every Kokoro-kind renderer shares
/// (<c>PronunciationRuleSet.FromContext</c>) will silently drop it for every persona-card-authored
/// rule exactly as <c>ActivePersonaCorrectionsCache</c> does today for corrections.
/// </para>
/// </summary>
public sealed record PronunciationRule(string Pattern, string Word, string Ipa);
