using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>Reads the arguments after <c>render</c> (SPEC F214.8). Any problem comes back as a plain message for exit code 2.</summary>
internal static class RenderOptionsParser
{
    internal const string DefaultRootFolder = "genwave-voiceoff";
    private const string DryRunFlag = "--dry-run";

    /// <summary>The default run folder: <c>~/genwave-voiceoff</c>.</summary>
    internal static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), DefaultRootFolder);

    /// <param name="args">The arguments after the verb.</param>
    internal static bool TryParse(IReadOnlyList<string> args, out RenderOptions? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = null;
        Dictionary<string, string> single = new(StringComparer.Ordinal);
        List<string> engines = [];
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
                continue;
            }

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Option '{name}' needs a value.";
                return false;
            }

            string value = args[++i];
            if (string.Equals(name, "--engine", StringComparison.Ordinal))
            {
                if (engines.Contains(value, StringComparer.Ordinal))
                {
                    error = $"Engine '{value}' was given more than once.";
                    return false;
                }

                engines.Add(value);
            }
            else if (!single.TryAdd(name, value))
            {
                error = $"Option '{name}' was given more than once.";
                return false;
            }
        }

        return TryBuild(single, engines, dryRun, out options, out error);
    }

    private static bool IsKnown(string name) =>
        name is "--tier" or "--engine" or "--root" or "--engines" or "--gauntlet" or DryRunFlag;

    private static bool TryBuild(Dictionary<string, string> single, List<string> engines, bool dryRun, out RenderOptions? options, out string? error)
    {
        options = null;
        if (!single.TryGetValue("--tier", out string? tierText))
        {
            error = "Option '--tier' is required: cpu or gpu.";
            return false;
        }

        EngineTier tier;
        switch (tierText)
        {
            case "cpu":
                tier = EngineTier.Cpu;
                break;
            case "gpu":
                tier = EngineTier.Gpu;
                break;
            default:
                error = $"Tier '{tierText}' is not cpu or gpu.";
                return false;
        }

        string baseDirectory = AppContext.BaseDirectory;
        options = new RenderOptions(
            tier,
            engines,
            Path.GetFullPath(single.GetValueOrDefault("--root") ?? DefaultRoot()),
            Path.GetFullPath(single.GetValueOrDefault("--engines") ?? Path.Combine(baseDirectory, "engines")),
            Path.GetFullPath(single.GetValueOrDefault("--gauntlet") ?? Path.Combine(baseDirectory, "gauntlet")),
            dryRun);
        error = null;
        return true;
    }
}
