using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Reeling;

/// <summary>Reads the arguments after <c>reel</c>; the same options, defaults and messages as <c>pair</c>, minus the seed. Any problem comes back as a plain message for exit code 2.</summary>
internal static class ReelOptionsParser
{
    internal static bool TryParse(IReadOnlyList<string> args, out ReelOptions? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = null;
        Dictionary<string, string> single = new(StringComparer.Ordinal);

        for (int i = 0; i < args.Count; i++)
        {
            string name = args[i];
            if (name is not ("--run" or "--root" or "--engines" or "--gauntlet"))
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

        if (!single.TryGetValue("--run", out string? runId))
        {
            error = "Option '--run' is required: the run id to reel, e.g. --run 20261022T193000Z-cpu.";
            return false;
        }

        if (!RunId.TryParseTier(runId, out _))
        {
            error = $"'{runId}' is not a run id like 20261022T193000Z-cpu.";
            return false;
        }

        string baseDirectory = AppContext.BaseDirectory;
        options = new ReelOptions(
            runId,
            Path.GetFullPath(single.GetValueOrDefault("--root") ?? RenderOptionsParser.DefaultRoot()),
            Path.GetFullPath(single.GetValueOrDefault("--engines") ?? Path.Combine(baseDirectory, "engines")),
            Path.GetFullPath(single.GetValueOrDefault("--gauntlet") ?? Path.Combine(baseDirectory, "gauntlet")));
        error = null;
        return true;
    }
}
