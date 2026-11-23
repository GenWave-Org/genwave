using GenWave.Core.TalkRules;
using GenWave.TalkRules;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Architecture.Tests.TalkRules.Laws;

/// <summary>
/// Law R4 (F220.6): for every rule X and fixture snapshot, the verdicts with X off equal the verdicts with
/// everything on, minus X's. Runs through the real <see cref="TalkRuleBook"/> and <see cref="InMemoryTalkRuleSwitches"/>.
/// "Verdicts" means the used ones (<see cref="RuleVerdict.SwitchedOn"/>): an off rule's verdict is recorded but not used (F220.29).
/// The rules are built afresh for every pass, handed the pass's switches, so no state leaks between passes.
/// </summary>
public static class SwitchOffIsHonestLaw
{
    public static async Task<IReadOnlyList<string>> ViolationsAsync(
        Func<ITalkRuleSwitches, IReadOnlyList<ITalkRule>> rulesFor,
        IReadOnlyList<TalkSnapshot> snapshots)
    {
        var violations = new List<string>();
        var allOn = await UsedAsync(rulesFor, snapshots, switchedOff: null);
        foreach (var off in rulesFor(new InMemoryTalkRuleSwitches()))
        {
            var withOff = await UsedAsync(rulesFor, snapshots, off.Id);
            for (var index = 0; index < snapshots.Count; index++)
            {
                var expected = allOn[index].Where(used => used.RuleId != off.Id).ToList();
                var actual = withOff[index];
                var changed = actual.Except(expected).Concat(expected.Except(actual)).Select(used => used.RuleId).Distinct().Order(StringComparer.Ordinal).ToList();
                if (changed.Count > 0)
                {
                    violations.Add($"R4 switching {off.Id} off changed the verdicts of {string.Join(", ", changed)} on snapshot {index}.");
                }
            }
        }

        return violations;
    }

    private static async Task<IReadOnlyList<IReadOnlyList<(string RuleId, RuleFlag? Flag)>>> UsedAsync(
        Func<ITalkRuleSwitches, IReadOnlyList<ITalkRule>> rulesFor,
        IReadOnlyList<TalkSnapshot> snapshots,
        string? switchedOff)
    {
        var switches = new InMemoryTalkRuleSwitches();
        var rules = rulesFor(switches);
        foreach (var rule in rules)
        {
            foreach (var kind in new[] { TalkKinds.DjLine, TalkKinds.Chat, TalkKinds.Ad }.Where(kind => rule.CanCover.HasFlag(kind)))
            {
                await switches.SetAsync(rule.Id, kind, rule.Id != switchedOff, CancellationToken.None);
            }
        }

        var book = new TalkRuleBook(rules, switches, NullLogger<TalkRuleBook>.Instance);
        var used = new List<IReadOnlyList<(string RuleId, RuleFlag? Flag)>>();
        foreach (var snapshot in snapshots)
        {
            var verdicts = await book.JudgeAsync(snapshot, CancellationToken.None);
            used.Add(verdicts.Where(verdict => verdict.SwitchedOn).Select(verdict => (verdict.RuleId, verdict.Flag)).ToList());
        }

        return used;
    }
}
