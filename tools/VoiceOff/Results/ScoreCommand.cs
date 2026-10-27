using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Serving;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Score;

/// <summary>
/// <c>voiceoff score</c> (SPEC F217.9): reads a run's cards, run.json, renders.jsonl and judgement.json and writes RESULTS.md. Exit 0 when written,
/// 2 for a usage error, 1 when the run was rendered from another script, nothing has been judged, or a file is missing or bad. This is the only place that does IO;
/// the text comes from <see cref="ResultsMarkdown"/>.
/// </summary>
internal static class ScoreCommand
{
    internal const int Ok = 0;
    internal const int Failed = 1;
    internal const int UsageError = 2;

    /// <param name="args">The arguments after the <c>score</c> verb.</param>
    internal static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!ScoreOptionsParser.TryParse(args, out ScoreOptions? options, out string? usage))
        {
            error.WriteLine(usage);
            return UsageError;
        }

        if (options.OutPath is null)
        {
            error.WriteLine($"Can't find tools/VoiceOff above {AppContext.BaseDirectory}; pass --out <file>.");
            return Failed;
        }

        try
        {
            (ScoreInput? input, string? problem) = await LoadAsync(options, ct);
            if (input is null)
            {
                error.WriteLine(problem);
                return Failed;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(options.OutPath) ?? ".");
            await AtomicTextFile.WriteAsync(options.OutPath, ResultsMarkdown.Render(input), ct);
            output.WriteLine($"Wrote {options.OutPath}");
            return Ok;
        }
        catch (Exception ex) when (ex is InvalidDataException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine(ex.Message);
            return Failed;
        }
    }

    private static async Task<(ScoreInput? Input, string? Problem)> LoadAsync(ScoreOptions options, CancellationToken ct)
    {
        RunLayout layout = new(options.Root, options.RunId);
        if (!File.Exists(layout.RunJsonPath))
        {
            return (null, $"Run {options.RunId} has no run.json under {options.Root}; render it first: voiceoff render --run {options.RunId}");
        }

        RunManifest manifest = await RunManifest.ReadAsync(layout.RunJsonPath, ct);
        string currentHash = ScriptFile.Load(options.ScriptPath).Hash;
        if (!string.Equals(manifest.ScriptHash, currentHash, StringComparison.Ordinal))
        {
            return (null, $"The run was rendered from script {manifest.ScriptHash} but the script is now {currentHash}.");
        }

        string judgementPath = layout.JudgementPath;
        Judgement? judgement = await new JudgementStore(judgementPath, TimeProvider.System).ReadAsync(ct);
        if (judgement is null)
        {
            return (null, $"Nothing has been judged yet: {judgementPath} does not exist.");
        }

        CardSet cards = CardSet.Load(options.CardsDirectory, EngineProfileSet.Load(options.EnginesDirectory));
        IReadOnlyList<RenderRecord> renders = await new RenderLog(layout.RendersPath).ReadAsync(ct);
        return (new ScoreInput(manifest, cards.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal), judgement, renders), null);
    }
}
