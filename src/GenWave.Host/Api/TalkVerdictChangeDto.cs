namespace GenWave.Host.Api;

/// <summary><paramref name="LegacyOutcome"/> is the stored kebab-case text ("re-asked"), null when the row has none.</summary>
public sealed record TalkVerdictChangeDto(
    long CheckId, string RuleId, TalkRuleFlagDto? Stored, TalkRuleFlagDto? Replayed, string? LegacyOutcome);
