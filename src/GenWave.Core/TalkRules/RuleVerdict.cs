namespace GenWave.Core.TalkRules;

/// <summary>
/// One rule's verdict on one attempt. Recorded for switched-off rules too, with <paramref name="SwitchedOn"/>
/// false (F220.29). A judge that throws is a flag with reason "Could not check" (F220.9).
/// <paramref name="Broken"/> is the other half of F220.9: a shaper or recorder that threw ("Could not shape" /
/// "Could not record"). It is never a flag, never changes the outcome, and is absent from the stored verdicts when null.
/// </summary>
public sealed record RuleVerdict(string RuleId, bool SwitchedOn, RuleFlag? Flag, string? Broken = null);
