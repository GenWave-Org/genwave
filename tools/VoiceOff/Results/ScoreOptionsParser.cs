using System.Diagnostics.CodeAnalysis;
using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Score;

/// <summary>Reads the arguments after <c>score</c>. Any problem comes back as a plain message for exit code 2.</summary>
internal static class ScoreOptionsParser
{
    internal static bool TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out ScoreOptions? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = null;
        Dictionary<string, string> given = new(StringComparer.Ordinal);

        for (int i = 0; i < args.Count; i++)
        {
            string name = args[i];
            if (name is not ("--run" or "--root" or "--cards" or "--engines" or "--script" or "--out"))
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

            if (!given.TryAdd(name, args[++i]))
            {
                error = $"Option '{name}' was given more than once.";
                return false;
            }
        }

        if (!given.TryGetValue("--run", out string? runId))
        {
            error = "Option '--run' is required: the run to score, e.g. --run first.";
            return false;
        }

        if (!RunId.IsValid(runId))
        {
            error = $"'{runId}' is not a run id ({RunId.Rule}).";
            return false;
        }

        // The same defaults render uses.
        options = new ScoreOptions(
            runId,
            Path.GetFullPath(given.GetValueOrDefault("--root") ?? RenderOptionsParser.DefaultRoot()),
            Path.GetFullPath(given.GetValueOrDefault("--engines") ?? RenderOptionsParser.DefaultEngines()),
            Path.GetFullPath(given.GetValueOrDefault("--cards") ?? RenderOptionsParser.DefaultCards()),
            Path.GetFullPath(given.GetValueOrDefault("--script") ?? RenderOptionsParser.DefaultScript()),
            given.TryGetValue("--out", out string? outPath) ? Path.GetFullPath(outPath) : ResultsPath.Default());
        error = null;
        return true;
    }
}
