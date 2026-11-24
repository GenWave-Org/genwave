namespace GenWave.Core.TalkRules;

/// <summary>A stored verdict that today's rules (or, when comparing, the legacy outcome) no longer match.</summary>
public sealed record TalkVerdictChange(
    long CheckId,
    string RuleId,
    RuleFlag? Stored,
    RuleFlag? Replayed,
    TalkLegacyOutcome? LegacyOutcome);
