using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Pairing;

/// <summary>
/// <c>voiceoff pair</c> (SPEC F214.9): builds the blind A/B pairs of a finished render run, copies the clips under opaque names into
/// <c>session/</c>, and writes the truth table to <c>manifest.json</c> only. Exit 0 when written, 2 for a usage error, 1 for missing or
/// bad input. A run that is already paired is refused: pairing again after votes would scramble the truth.
/// </summary>
internal static class PairCommand
{
    internal const int Ok = 0;
    internal const int Failed = 1;
    internal const int UsageError = 2;

    /// <param name="args">The arguments after the <c>pair</c> verb.</param>
    internal static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!PairOptionsParser.TryParse(args, out PairOptions? options, out string? usage) || options is null)
        {
            error.WriteLine(usage);
            return UsageError;
        }

        try
        {
            return await PairAsync(options, output, error, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or DirectoryNotFoundException or JsonException)
        {
            error.WriteLine(ex.Message);
            return Failed;
        }
    }

    private static async Task<int> PairAsync(PairOptions options, TextWriter output, TextWriter error, CancellationToken ct)
    {
        RunLayout layout = new(options.Root, options.RunId);
        string manifestPath = Path.Combine(layout.RunDirectory, "manifest.json");
        if (!Directory.Exists(layout.RunDirectory))
        {
            throw new InvalidDataException($"Run {options.RunId} was not found under {options.Root}.");
        }

        if (File.Exists(manifestPath))
        {
            error.WriteLine($"run {options.RunId} is already paired");
            return Failed;
        }

        if (Directory.Exists(SessionFolder.PathIn(layout)))
        {
            error.WriteLine($"run {options.RunId} is already paired: session/ exists but manifest.json is missing. Delete {SessionFolder.PathIn(layout)} to retry.");
            return Failed;
        }

        EngineProfileSet profiles = EngineProfileSet.Load(options.EnginesDirectory);
        RunManifest info = StrictJson.Read<RunManifest>(await ReadAsync(layout.RunJsonPath, "run.json", ct));
        GauntletSet gauntlet = GauntletSet.Load(options.GauntletDirectory);
        if (!string.Equals(gauntlet.ContentHash, info.GauntletHash, StringComparison.Ordinal))
        {
            error.WriteLine($"The gauntlet in {options.GauntletDirectory} has hash {gauntlet.ContentHash} but run {options.RunId} used {info.GauntletHash}.");
            return Failed;
        }

        GatesFile gates = StrictJson.Read<GatesFile>(await ReadAsync(layout.GatesJsonPath, "gates.json", ct));
        CanonicalClips canonical = new();
        UsableClips clips = new(layout, canonical, await new TimingLog(layout.TimingPath).ReadAsync(ct));
        Random rng = new(options.Seed ?? RandomNumberGenerator.GetInt32(int.MaxValue));
        PairPlan plan = PairPlanner.Plan(profiles.Baseline.Slug, Challengers(profiles, gates), gauntlet.Lines, clips.IsUsable, rng);
        IReadOnlyList<IReadOnlyList<PlannedPair>> sessions = SessionCutter.Cut(plan.Pairs, rng);

        IReadOnlyList<ManifestPair> truth = await new SessionFolder(layout, profiles.Baseline.Slug, canonical).WriteAsync(sessions, ct);
        await AtomicTextFile.WriteAsync(manifestPath, StrictJson.Write(new PairManifest(info.RunId, info.GauntletHash, truth, [])), ct);

        Report(output, plan, sessions.Count, layout);
        return Ok;
    }

    /// <summary>The non-baseline profiles that passed BOTH gates in this run. An engine that failed either gate gets no pairs.</summary>
    private static IEnumerable<EngineProfile> Challengers(EngineProfileSet profiles, GatesFile gates) => profiles.Profiles
        .Where(p => !p.Baseline)
        .Where(p => gates.Engines.Any(g => string.Equals(g.Slug, p.Slug, StringComparison.Ordinal) && g.Container?.Passed == true && g.VoiceSelect?.Passed == true));

    private static async Task<string> ReadAsync(string path, string name, CancellationToken ct) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : throw new InvalidDataException($"The run has no {name}.");

    private static void Report(TextWriter output, PairPlan plan, int sessions, RunLayout layout)
    {
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Paired {plan.Pairs.Count} (skipped {plan.Skipped} pairs with a failed clip) into {sessions} session{(sessions == 1 ? string.Empty : "s")} -> {SessionFolder.PathIn(layout)}"));
        for (int i = 1; i <= sessions; i++)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Session {i}: ?s={i}"));
        }
    }
}
