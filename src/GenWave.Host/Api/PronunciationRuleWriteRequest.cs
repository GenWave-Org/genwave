namespace GenWave.Host.Api;

/// <summary>
/// Body of <c>POST /api/pronunciations</c> and <c>PUT /api/pronunciations?pattern=&amp;word=</c>
/// (SPEC F97.1, STORY-254): a candidate station pronunciation rule. <see cref="Word"/> is optional —
/// a blank/absent value defaults to <see cref="Pattern"/> at compile time, mirroring
/// <c>GenWave.Tts.PronunciationRule.Parse</c>'s own "MacLeod needs no context" default (F97.1). On a
/// <c>PUT</c> this is the NEW shape a rule is replaced with — its resolved (Pattern, Word) identity
/// may differ from the query's target identity (a rename), and is rejected with 409 if it collides
/// with a DIFFERENT existing station rule (T144 review finding F1/F2).
/// </summary>
public sealed record PronunciationRuleWriteRequest(string Pattern, string? Word, string Ipa);
