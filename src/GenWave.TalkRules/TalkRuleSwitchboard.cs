using System.Numerics;
using GenWave.Core.TalkRules;

namespace GenWave.TalkRules;

/// <summary>
/// The one door for changing a switch. Refuses in both directions: a kind outside the rule's <c>CanCover</c> can be
/// neither switched on nor off, since the rule book never reads such a row. The store stays dumb.
/// </summary>
public sealed class TalkRuleSwitchboard(IEnumerable<ITalkRule> rules, ITalkRuleSwitches switches) : ITalkRuleSwitchboard
{
    public async ValueTask<TalkRuleSwitchResult> SwitchAsync(string ruleId, TalkKinds kind, bool enabled, CancellationToken ct)
    {
        var rule = rules.FirstOrDefault(candidate => string.Equals(candidate.Id, ruleId, StringComparison.Ordinal));
        if (rule is null)
        {
            return TalkRuleSwitchResult.UnknownRule;
        }

        var isOneKind = kind > TalkKinds.None && BitOperations.IsPow2((int)kind);
        if (!isOneKind || !rule.CanCover.HasFlag(kind))
        {
            return TalkRuleSwitchResult.KindNotCovered;
        }

        await switches.SetAsync(rule.Id, kind, enabled, ct);
        return TalkRuleSwitchResult.Stored;
    }
}
