using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GenWave.VoiceOff.Audio;
using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Pairing;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Score;
using GenWave.VoiceOff.Scoring;
using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Reeling;

/// <summary>
/// <c>voiceoff reel</c> (SPEC F214.13): builds the wince reels of a paired run and lists them in <c>manifest.json</c> under opaque ids. Exit 0 when built,
/// 2 for a usage error, 1 for missing or bad input. The reel-to-engine map goes to <c>manifest.json</c> only, never to the console: Dean is the judge.
/// A run that already has reels is refused.
/// </summary>
internal static class ReelCommand
{
    internal const int Ok = 0;
    internal const int Failed = 1;
    internal const int UsageError = 2;

    /// <param name="args">The arguments after the <c>reel</c> verb.</param>
    internal static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!ReelOptionsParser.TryParse(args, out ReelOptions? options, out string? usage) || options is null)
        {
            error.WriteLine(usage);
            return UsageError;
        }

        try
        {
            return await ReelAsync(options, output, error, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or DirectoryNotFoundException or JsonException)
        {
            error.WriteLine(ex.Message);
            return Failed;
        }
    }

    private static async Task<int> ReelAsync(ReelOptions options, TextWriter output, TextWriter error, CancellationToken ct)
    {
        RunLayout layout = new(options.Root, options.RunId);
        EngineProfileSet profiles = EngineProfileSet.Load(options.EnginesDirectory);
        TierRun run = await ScoreRunLoader.LoadAsync(options.Root, options.RunId, profiles, ct);
        if (!Directory.Exists(SessionFolder.PathIn(layout)))
        {
            error.WriteLine($"Run {options.RunId} is not paired. Run `voiceoff pair --run {options.RunId}` first.");
            return Failed;
        }

        if (run.Manifest.Reels.Count > 0 || Directory.Exists(ReelBuilder.PathIn(layout)))
        {
            error.WriteLine($"Run {options.RunId} already has reels.");
            return Failed;
        }

        GauntletSet gauntlet = GauntletSet.Load(options.GauntletDirectory);
        if (!string.Equals(gauntlet.ContentHash, run.GauntletHash, StringComparison.Ordinal))
        {
            error.WriteLine($"The gauntlet in {options.GauntletDirectory} has hash {gauntlet.ContentHash} but run {options.RunId} used {run.GauntletHash}.");
            return Failed;
        }

        Matrix matrix = Matrix.Build(run.Manifest, run.Votes, profiles.Baseline.Slug);
        WinceChoice choice = WinceSet.Choose(profiles, run.Gates, matrix);
        UsableClips clips = new(layout, new CanonicalClips(), run.Timing);

        // Built in a random order, so file times and the manifest order say nothing about which reel is the winner.
        IReadOnlyList<string> buildOrder = Shuffled(choice.Engines);
        (IReadOnlyList<ManifestReel> reels, int leftOut) = await new ReelBuilder(layout, clips, new LoudnessMatch()).BuildAsync(buildOrder, gauntlet.Lines, ct);

        PairManifest manifest = run.Manifest with { Reels = reels };
        try
        {
            await AtomicTextFile.WriteAsync(Path.Combine(layout.RunDirectory, "manifest.json"), StrictJson.Write(manifest), ct);
        }
        catch
        {
            // Reels the manifest does not list would make the run look reeled (and refuse a retry), so take them away.
            Directory.Delete(ReelBuilder.PathIn(layout), recursive: true);
            throw;
        }

        Report(output, choice, reels.Count, leftOut, layout);
        return Ok;
    }

    /// <summary>A CSPRNG shuffle.</summary>
    private static List<T> Shuffled<T>(IReadOnlyList<T> source)
    {
        List<T> items = [.. source];
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }

        return items;
    }

    private static void Report(TextWriter output, WinceChoice choice, int reels, int leftOut, RunLayout layout)
    {
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Built {reels} wince reels -> {ReelBuilder.PathIn(layout)}"));
        if (choice.IsShort)
        {
            output.WriteLine("Only one challenger passed both gates, so the wince set has two reels.");
        }

        if (leftOut > 0)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Left out {leftOut} {(leftOut == 1 ? "clip" : "clips")} that did not render."));
        }
    }
}
