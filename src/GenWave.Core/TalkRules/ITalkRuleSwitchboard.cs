namespace GenWave.Core.TalkRules;

/// <summary>Switches a rule on or off for one kind of talk, refusing what makes no sense (SPEC F220.22).</summary>
public interface ITalkRuleSwitchboard
{
    /// <summary>Stores the switch when the rule exists and can cover <paramref name="kind"/> (one single kind); otherwise stores nothing.</summary>
    ValueTask<TalkRuleSwitchResult> SwitchAsync(string ruleId, TalkKinds kind, bool enabled, CancellationToken ct);
}
