using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Score;

/// <summary>One tier, scored: its run, its matrix and its verdict.</summary>
internal sealed record TierScore(TierRun Run, Matrix Matrix, Verdict Verdict)
{
    internal static TierScore Build(EngineProfileSet profiles, TierRun run)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(run);
        string baseline = profiles.Baseline.Slug;
        Matrix matrix = Matrix.Build(run.Manifest, run.Votes, baseline);
        return new TierScore(run, matrix, Verdict.Decide(matrix, baseline, CountedWinces(run)));
    }

    /// <summary>Each engine's reel total: the last entry per reel in file order.</summary>
    internal static IReadOnlyDictionary<string, int> CountedWinces(TierRun run)
    {
        Dictionary<string, int> byReel = new(StringComparer.Ordinal);
        foreach (WinceEntry entry in run.Winces)
        {
            byReel[entry.ReelId] = entry.Count;
        }

        Dictionary<string, int> byEngine = new(StringComparer.Ordinal);
        foreach (ManifestReel reel in run.Manifest.Reels)
        {
            if (byReel.TryGetValue(reel.ReelId, out int count))
            {
                byEngine[reel.Engine] = count;
            }
        }

        return byEngine;
    }
}
