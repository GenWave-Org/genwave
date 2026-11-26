namespace GenWave.Core.TalkRules;

/// <summary>How many attempts of one kind of talk one rule flagged, and how many its shaper or recorder broke on (F220.9).</summary>
public sealed record TalkRuleVerdictCount(string RuleId, TalkKinds Kind, int Flagged, int Broken);
