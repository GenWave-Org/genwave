using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Laws;

/// <summary>Judges snapshots directly (no rule book) and flattens the result to a sorted, comparable set of lines.</summary>
public static class JudgeVerdicts
{
    public static IReadOnlyList<string> Collect(IEnumerable<IJudgingTalkRule> judges, IReadOnlyList<TalkSnapshot> snapshots)
    {
        var lines = new List<string>();
        foreach (var judge in judges)
        {
            for (var index = 0; index < snapshots.Count; index++)
            {
                if (!judge.CanCover.HasFlag(snapshots[index].Kind))
                {
                    continue;
                }

                var flag = judge.Judge(snapshots[index]);
                lines.Add($"{judge.Id} @ snapshot {index}: {(flag is null ? "pass" : $"flag {flag.Reason} / {flag.Ask} / {flag.Evidence}")}");
            }
        }

        return lines.Order(StringComparer.Ordinal).ToList();
    }
}
