using System.Globalization;
using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Pairing;

/// <summary>Reads the arguments after <c>pair</c>. Any problem comes back as a plain message for exit code 2.</summary>
internal static class PairOptionsParser
{
    internal static bool TryParse(IReadOnlyList<string> args, out PairOptions? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = null;
        Dictionary<string, string> single = new(StringComparer.Ordinal);

        for (int i = 0; i < args.Count; i++)
        {
            string name = args[i];
            if (name is not ("--run" or "--root" or "--engines" or "--gauntlet" or "--seed"))
            {
                error = name.StartsWith("--", StringComparison.Ordinal)
                    ? $"Unknown option '{name}'. Run 'voiceoff --help' to see the verbs."
                    : $"Unexpected argument '{name}'. Options start with '--'.";
                return false;
            }

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Option '{name}' needs a value.";
                return false;
            }

            if (!single.TryAdd(name, args[++i]))
            {
                error = $"Option '{name}' was given more than once.";
                return false;
            }
        }

        return TryBuild(single, out options, out error);
    }

    private static bool TryBuild(Dictionary<string, string> single, out PairOptions? options, out string? error)
    {
        options = null;
        if (!single.TryGetValue("--run", out string? runId))
        {
            error = "Option '--run' is required: the run id to pair, e.g. --run 20261022T193000Z-cpu.";
            return false;
        }

        if (!RunId.TryParseTier(runId, out _))
        {
            error = $"'{runId}' is not a run id like 20261022T193000Z-cpu.";
            return false;
        }

        int? seed = null;
        if (single.TryGetValue("--seed", out string? seedText))
        {
            if (!int.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                error = "Option '--seed' needs a whole number from 0 to 2147483647.";
                return false;
            }

            seed = value;
        }

        string baseDirectory = AppContext.BaseDirectory;
        options = new PairOptions(
            runId,
            Path.GetFullPath(single.GetValueOrDefault("--root") ?? RenderOptionsParser.DefaultRoot()),
            Path.GetFullPath(single.GetValueOrDefault("--engines") ?? Path.Combine(baseDirectory, "engines")),
            Path.GetFullPath(single.GetValueOrDefault("--gauntlet") ?? Path.Combine(baseDirectory, "gauntlet")),
            seed);
        error = null;
        return true;
    }
}
