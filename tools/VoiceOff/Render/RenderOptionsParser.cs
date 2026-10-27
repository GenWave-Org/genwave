namespace GenWave.VoiceOff.Render;

/// <summary>Reads the arguments after <c>render</c> (SPEC F217.5). Any problem comes back as a plain message for exit code 2.</summary>
internal static class RenderOptionsParser
{
    internal const string DefaultRootFolder = "genwave-voiceoff";
    private const string DryRunFlag = "--dry-run";

    /// <summary>The default run folder: <c>~/genwave-voiceoff</c>.</summary>
    internal static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), DefaultRootFolder);

    /// <summary>The engine profiles shipped beside the tool.</summary>
    internal static string DefaultEngines() => Path.Combine(AppContext.BaseDirectory, "engines");

    /// <summary>The script shipped beside the tool.</summary>
    internal static string DefaultScript() => Path.Combine(AppContext.BaseDirectory, "script", "script.json");

    /// <summary>The card files shipped beside the tool.</summary>
    internal static string DefaultCards() => Path.Combine(AppContext.BaseDirectory, "cards");

    /// <param name="args">The arguments after the verb.</param>
    internal static bool TryParse(IReadOnlyList<string> args, out RenderOptions? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = null;
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool dryRun = false;

        for (int i = 0; i < args.Count; i++)
        {
            string name = args[i];
            if (!IsKnown(name))
            {
                error = name.StartsWith("--", StringComparison.Ordinal)
                    ? $"Unknown option '{name}'. Run 'voiceoff --help' to see the verbs."
                    : $"Unexpected argument '{name}'. Options start with '--'.";
                return false;
            }

            if (string.Equals(name, DryRunFlag, StringComparison.Ordinal))
            {
                if (dryRun)
                {
                    error = $"Option '{name}' was given more than once.";
                    return false;
                }

                dryRun = true;
            }
            else if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Option '{name}' needs a value.";
                return false;
            }
            else if (!values.TryAdd(name, args[++i]))
            {
                error = $"Option '{name}' was given more than once.";
                return false;
            }
        }

        return TryBuild(values, dryRun, out options, out error);
    }

    private static bool IsKnown(string name) =>
        name is "--run" or "--card" or "--root" or "--engines" or "--cards" or "--script" or DryRunFlag;

    private static bool TryBuild(Dictionary<string, string> values, bool dryRun, out RenderOptions? options, out string? error)
    {
        options = null;
        if (!values.TryGetValue("--run", out string? runId))
        {
            error = "Option '--run' is required: the name of the run folder, such as 'first'.";
            return false;
        }

        if (!RunId.IsValid(runId))
        {
            error = $"Run id '{runId}' is not usable as a folder name: {RunId.Rule}.";
            return false;
        }

        options = new RenderOptions(
            runId,
            values.GetValueOrDefault("--card"),
            Path.GetFullPath(values.GetValueOrDefault("--root") ?? DefaultRoot()),
            Path.GetFullPath(values.GetValueOrDefault("--engines") ?? DefaultEngines()),
            Path.GetFullPath(values.GetValueOrDefault("--cards") ?? DefaultCards()),
            Path.GetFullPath(values.GetValueOrDefault("--script") ?? DefaultScript()),
            dryRun);
        error = null;
        return true;
    }
}
