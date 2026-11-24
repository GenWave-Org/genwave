namespace GenWave.Core.TalkRules;

/// <summary>
/// One rule's verdict on one attempt. Recorded for switched-off rules too, with <paramref name="SwitchedOn"/>
/// false (F220.29). A rule that throws is a flag with reason "Could not check" (F220.9).
/// </summary>
public sealed record RuleVerdict(string RuleId, bool SwitchedOn, RuleFlag? Flag);
