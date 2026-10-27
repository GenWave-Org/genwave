using GenWave.VoiceOff.Text;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>
/// <c>voiceoff render</c> (SPEC F214.1 to F214.8): the gauntlet through every selected engine, loudness-matched into a new
/// run folder with <c>run.json</c> and <c>timing.jsonl</c>. An engine that is not attempted, unreachable or failing its container gate is a row in the output, never a failed run.
/// </summary>
internal static class RenderCommand
{
    internal const int Ok = 0;
    internal const int Failed = 1;
    internal const int UsageError = 2;
    internal const int Interrupted = 130;

    internal const string CorrectionsPendingNotice =
        "Corrections are pending: export the station's corrections before trusting pronunciation results.";

    /// <param name="args">The arguments after the <c>render</c> verb.</param>
    internal static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, RenderServices services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(services);

        if (!RenderOptionsParser.TryParse(args, out RenderOptions? options, out string? usage) || options is null)
        {
            error.WriteLine(usage);
            return UsageError;
        }

        if (!TryLoad(options, error, out EngineProfileSet? profiles, out GauntletSet? set) || profiles is null || set is null)
        {
            return Failed;
        }

        string? unknown = options.Engines.FirstOrDefault(slug => profiles.Profiles.All(p => !string.Equals(p.Slug, slug, StringComparison.Ordinal)));
        if (unknown is not null)
        {
            error.WriteLine($"Unknown engine '{unknown}'. Known engines: {string.Join(", ", profiles.Profiles.Select(p => p.Slug))}.");
            return UsageError;
        }

        if (options.DryRun)
        {
            return await DryRunReport.RunAsync(options, SelectEngines(profiles, options), set, output, ct);
        }

        try
        {
            return await RunLoadedAsync(options, profiles, set, output, error, services, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            output.WriteLine("Cancelled; the files written so far are kept.");
            return Interrupted;
        }
    }

    private static bool TryLoad(RenderOptions options, TextWriter error, out EngineProfileSet? profiles, out GauntletSet? set)
    {
        profiles = null;
        set = null;
        try
        {
            profiles = EngineProfileSet.Load(options.EnginesDirectory);
            set = GauntletSet.Load(options.GauntletDirectory);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or DirectoryNotFoundException)
        {
            error.WriteLine(ex.Message);
            return false;
        }
    }

    private static async Task<int> RunLoadedAsync(
        RenderOptions options, EngineProfileSet profiles, GauntletSet set, TextWriter output, TextWriter error, RenderServices services, CancellationToken ct)
    {
        if (set.CorrectionsPending)
        {
            output.WriteLine(CorrectionsPendingNotice);
        }

        DateTimeOffset startedAt = services.Time.GetUtcNow();
        RunLayout layout = new(options.Root, RunId.Format(startedAt, options.Tier));
        if (Directory.Exists(layout.RunDirectory))
        {
            error.WriteLine($"The run folder {layout.RunDirectory} already exists; wait a second and run again.");
            return Failed;
        }

        try
        {
            Directory.CreateDirectory(layout.RunDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Cannot create the run folder {layout.RunDirectory}: {ex.Message}");
            return Failed;
        }

        await new RunManifest(layout.RunId, RunId.TierName(options.Tier), services.HostName, startedAt, set.ContentHash, set.CorrectionsPending)
            .WriteAsync(layout.RunJsonPath, ct);
        output.WriteLine($"Run {layout.RunId} -> {layout.RunDirectory}");

        EngineProfile[] selected = SelectEngines(profiles, options);
        EngineRun run = new(services, options, set, layout, new TimingLog(layout.TimingPath), new RunProgress(selected.Sum(p => RunProgress.ClipsFor(p, set))), output);
        run.DropTierSkips(selected);
        foreach (EngineProfile profile in selected)
        {
            output.WriteLine(await run.RunAsync(profile, ct));
        }

        return Ok;
    }

    /// <summary>The engines the run was asked for (all when none was named), in slug order.</summary>
    private static EngineProfile[] SelectEngines(EngineProfileSet profiles, RenderOptions options) => profiles.Profiles
        .Where(p => options.Engines.Count == 0 || options.Engines.Contains(p.Slug, StringComparer.Ordinal))
        .OrderBy(p => p.Slug, StringComparer.Ordinal)
        .ToArray();
}
