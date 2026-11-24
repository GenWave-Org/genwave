namespace GenWave.Host.Api;

public sealed record TalkRulesReplayResponse(
    IReadOnlyList<TalkRuleCountDto> Counts, IReadOnlyList<TalkVerdictChangeDto> Changes, int TrimSavedLines);
