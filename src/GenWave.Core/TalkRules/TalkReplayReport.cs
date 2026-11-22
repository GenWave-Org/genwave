namespace GenWave.Core.TalkRules;

/// <summary><paramref name="TrimSavedLines"/> counts lines the legacy trim saved that the rules would have re-asked (F220.20); zero unless legacy was compared.</summary>
public sealed record TalkReplayReport(
    TalkList<TalkRuleReplayCount> Counts,
    TalkList<TalkVerdictChange> Changes,
    int TrimSavedLines);
