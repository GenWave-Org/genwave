namespace GenWave.Host.Api;

/// <summary>
/// One entry of <c>POST /api/tts/preview</c>'s optional <c>candidateRules</c> array (SPEC F126.1,
/// STORY-323 AC2/AC4): a pronunciation rule the operator is authoring but has not saved yet. Same
/// shape and same optional-<see cref="Word"/> default as
/// <see cref="PronunciationRuleWriteRequest"/> (blank/absent defaults to <see cref="Pattern"/>,
/// mirroring <c>GenWave.Tts.PronunciationRule.Parse</c>) — this is deliberately a SIBLING type, not
/// a reuse of <see cref="PronunciationRuleWriteRequest"/> itself: the two travel to different
/// endpoints with different failure postures (a rejected write here never touches
/// <c>Tts:Pronunciations</c>), and a future field on one must not silently ride along on the other.
/// </summary>
public sealed record TtsPreviewCandidateRule(string? Pattern, string? Word, string? Ipa);
