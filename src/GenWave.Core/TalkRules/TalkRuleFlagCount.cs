namespace GenWave.Core.TalkRules;

/// <summary>How many attempts of one kind of talk one rule flagged.</summary>
public sealed record TalkRuleFlagCount(string RuleId, TalkKinds Kind, int Count);
