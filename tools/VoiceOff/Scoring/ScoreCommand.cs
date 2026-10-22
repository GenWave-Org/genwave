using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Score;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Scoring;

/// <summary>
/// <c>voiceoff score</c> (SPEC F214.14, F214.15): loads the runs, scores them, and writes RESULTS.md. Exit 0 when the file is written,
/// 2 for a usage error, 1 for missing, bad or refused data. Nothing here touches the network or ffmpeg.
/// </summary>
internal static class ScoreCommand
{
    internal const int Ok = 0;
    internal const int Failed = 1;
    internal const int UsageError = 2;

    internal static readonly string DefaultOut = Path.Combine("tools", "VoiceOff", "RESULTS.md");

    /// <param name="args">The arguments after the <c>score</c> verb.</param>
    internal static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!ScoreOptionsParser.TryParse(args, out ScoreOptions? options, out string? usage) || options is null)
        {
            error.WriteLine(usage);
            return UsageError;
        }

        string outPath = Path.GetFullPath(options.Out ?? DefaultOut);
        if (options.Out is null && !Directory.Exists(Path.GetDirectoryName(outPath)))
        {
            error.WriteLine($"The folder for {DefaultOut} does not exist here; run from the repo root or pass --out <file>.");
            return UsageError;
        }

        try
        {
            ScoreInput input = await LoadAsync(options, ct);
            string? refusal = RunMergeCheck.Refusal(input.Runs);
            if (refusal is not null)
            {
                error.WriteLine(refusal);
                return Failed;
            }

            IReadOnlyList<TierScore> scores = ResultsMarkdown.Score(input);
            await AtomicTextFile.WriteAsync(outPath, ResultsMarkdown.Render(input, scores), ct);
            foreach (TierScore score in scores)
            {
                output.WriteLine($"{RunId.TierName(score.Run.Tier)}: {score.Verdict.Text}");
            }

            output.WriteLine($"Wrote {outPath}");
            return Ok;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine(ex.Message);
            return Failed;
        }
    }

    private static async Task<ScoreInput> LoadAsync(ScoreOptions options, CancellationToken ct)
    {
        EngineProfileSet profiles = EngineProfileSet.Load(options.EnginesDirectory);
        List<TierRun> runs = [];
        foreach (string runId in options.Runs)
        {
            runs.Add(await ScoreRunLoader.LoadAsync(options.Root, runId, profiles, ct));
        }

        return new ScoreInput(profiles, runs, options.BudgetSeconds);
    }
}
