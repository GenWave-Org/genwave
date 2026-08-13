namespace GenWave.Host.Api;

/// <summary>
/// The body of a successful <c>POST</c>/<c>PUT /api/pronunciations</c> (gh-#491): the written rule's
/// own row (<see cref="Rule"/>, exactly the shape <c>GET /api/pronunciations</c> lists it under) plus
/// zero or more authoring-time <see cref="Warnings"/> — currently the rules-over-corrections
/// collision notice (<see cref="GenWave.Tts.RuleOverCorrectionPrecedence"/>): the saved rule shares
/// its word with an existing speech correction, so that correction is suppressed on every render
/// where this rule is in play.
///
/// A warning is advisory, never a refusal — the collision can be exactly what the operator intends
/// (migrating a legacy respelling correction to an IPA rule edits the rule while the correction
/// still exists), so blocking the write would forbid the intended workflow (gh-#491 ruling). A
/// dedicated response record rather than a <c>Warnings</c> field on
/// <see cref="PronunciationRuleDto"/> itself: a warning is a fact about THIS write against THIS
/// moment's corrections, never a property of the stored rule — the same this-read-only reasoning
/// <see cref="GenWave.Tts.MergedPronunciationRule"/>'s provenance already follows.
/// </summary>
public sealed record PronunciationRuleWriteResponse(
    PronunciationRuleDto Rule, IReadOnlyList<string> Warnings);
