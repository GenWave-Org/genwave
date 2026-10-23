using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>The run's progress book: clips planned, clips done and the wall time of the real renders, across engines.</summary>
internal sealed class RunProgress(int plannedClips)
{
    private readonly List<double> doneWallMs = [];
    private int planned = plannedClips;
    private int done;

    /// <summary>The clips an attempted engine will render: every gauntlet line in every lane of its plan.</summary>
    internal static int ClipsFor(EngineProfile profile, GauntletSet set)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(set);
        return set.Lines.Count * LanePlan.For(profile).Count;
    }

    /// <summary>An engine that never started rendering: its clips leave the total.</summary>
    internal void Drop(int clips) => planned = Math.Max(planned - clips, done);

    internal string NextLine() => RenderProgress.Line(done + 1, planned, doneWallMs);

    /// <summary>A clip finished. A clip never rendered (the engine stopped answering) counts as done but adds no wall time.</summary>
    internal void Finished(ClipOutcome outcome, bool rendered)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        done++;
        if (rendered)
        {
            doneWallMs.Add(outcome.WallMs);
        }
    }
}
