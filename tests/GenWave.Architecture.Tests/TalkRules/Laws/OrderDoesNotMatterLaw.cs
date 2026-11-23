using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Laws;

/// <summary>
/// Law R3 (F220.5): fixture snapshots judged with the rules in every rotation and in 20 seeded shuffles give
/// the same verdict sets. Judges are called directly: the rule book sorts by id, which would hide an order leak.
/// The same rule instances are reused across orders, so a rule that remembers earlier calls is caught as well.
/// </summary>
public static class OrderDoesNotMatterLaw
{
    public const int Shuffles = 20;

    public static IReadOnlyList<string> Violations(IReadOnlyList<IJudgingTalkRule> judges, IReadOnlyList<TalkSnapshot> snapshots)
    {
        var baseline = JudgeVerdicts.Collect(judges, snapshots);
        var violations = new List<string>();
        foreach (var (label, order) in Orders(judges))
        {
            var verdicts = JudgeVerdicts.Collect(order, snapshots);
            if (!verdicts.SequenceEqual(baseline, StringComparer.Ordinal))
            {
                var changed = verdicts.Except(baseline, StringComparer.Ordinal).Concat(baseline.Except(verdicts, StringComparer.Ordinal)).ToList();
                var changedRules = judges.Where(judge => changed.Any(line => line.StartsWith(judge.Id + " @", StringComparison.Ordinal))).Select(judge => judge.Id);
                violations.Add($"R3 order {label} changed the verdicts of {string.Join(", ", changedRules)}: {string.Join("; ", changed)}");
            }
        }

        return violations;
    }

    private static IEnumerable<(string Label, IReadOnlyList<IJudgingTalkRule> Order)> Orders(IReadOnlyList<IJudgingTalkRule> judges)
    {
        for (var shift = 0; shift < judges.Count; shift++)
        {
            yield return ($"rotation {shift}", [.. judges.Skip(shift), .. judges.Take(shift)]);
        }

        for (var seed = 1; seed <= Shuffles; seed++)
        {
            var shuffled = judges.ToArray();
            new Random(seed).Shuffle(shuffled);
            yield return ($"shuffle {seed}", shuffled);
        }
    }
}
