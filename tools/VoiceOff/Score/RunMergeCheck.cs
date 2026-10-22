using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Score;

/// <summary>The rules for scoring several runs together: one gauntlet, one run per tier.</summary>
internal static class RunMergeCheck
{
    /// <summary>Why the runs cannot be merged, or null when they can.</summary>
    internal static string? Refusal(IReadOnlyList<TierRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        for (int i = 1; i < runs.Count; i++)
        {
            if (!string.Equals(runs[0].GauntletHash, runs[i].GauntletHash, StringComparison.Ordinal))
            {
                return $"Runs {runs[0].RunId} and {runs[i].RunId} used different gauntlets (hash {runs[0].GauntletHash} and hash {runs[i].GauntletHash}); score runs of the same gauntlet.";
            }
        }

        foreach (TierRun run in runs)
        {
            TierRun first = runs.First(r => r.Tier == run.Tier);
            if (!ReferenceEquals(first, run))
            {
                return $"Runs {first.RunId} and {run.RunId} are both {RunId.TierName(run.Tier)} runs; score one run per tier.";
            }
        }

        return null;
    }
}
