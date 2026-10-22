using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary><c>render --dry-run</c> (STORY-495): what a run would render and roughly how long it takes. Makes no request and writes no file.</summary>
internal static class DryRunReport
{
    internal static async Task<int> RunAsync(RenderOptions options, IReadOnlyList<EngineProfile> selected, GauntletSet set, TextWriter output, CancellationToken ct)
    {
        EngineProfile[] attempted = selected.Where(p => EngineRun.AttemptsTier(p, options.Tier)).ToArray();
        int clips = attempted.Sum(p => RunProgress.ClipsFor(p, set));
        string engines = attempted.Length == 1 ? "1 engine" : $"{attempted.Length} engines";
        output.WriteLine($"Dry run: {(clips == 1 ? "1 clip" : $"{clips} clips")} across {engines}");
        output.WriteLine((await PreviousRunFinder.FindAsync(options.Root, options.Tier, ct)).EtaLine(clips));
        foreach (EngineProfile skipped in selected.Except(attempted))
        {
            output.WriteLine($"{skipped.Slug}: not attempted at tier {RunId.TierName(options.Tier)}");
        }

        return RenderCommand.Ok;
    }
}
