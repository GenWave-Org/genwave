namespace GenWave.Core.TalkRules;

/// <summary>
/// One row per written attempt. <see cref="Cleaned"/> is null when clean-up failed; that attempt is still
/// recorded (F220.29).
/// </summary>
public sealed record TalkCheckRecord(
    long? PersonaId,
    TalkCheckMode Mode,
    string RawReply,
    string? Cleaned,
    TalkSnapshot Snapshot,
    TalkList<RuleVerdict> Verdicts,
    TalkLegacyOutcome? LegacyOutcome,
    TalkOutcome Outcome);
