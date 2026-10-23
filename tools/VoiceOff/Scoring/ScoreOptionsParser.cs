using System.Globalization;
using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Scoring;

/// <summary>Reads the arguments after <c>score</c>. Any problem comes back as a plain message for exit code 2.</summary>
internal static class ScoreOptionsParser
{
    internal const int MinBudgetSeconds = 1;
    internal const int MaxBudgetSeconds = 3600;

    internal static bool TryParse(IReadOnlyList<string> args, out ScoreOptions? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = null;
        Dictionary<string, string> single = new(StringComparer.Ordinal);
        List<string> runs = [];

        for (int i = 0; i < args.Count; i++)
        {
            string name = args[i];
            if (name is not ("--run" or "--root" or "--engines" or "--out" or "--budget-seconds"))
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

            string value = args[++i];
            if (string.Equals(name, "--run", StringComparison.Ordinal))
            {
                if (!RunId.TryParseTier(value, out _))
                {
                    error = $"'{value}' is not a run id like 20261022T193000Z-cpu.";
                    return false;
                }

                if (runs.Contains(value, StringComparer.Ordinal))
                {
                    error = $"Run '{value}' was given more than once.";
                    return false;
                }

                runs.Add(value);
            }
            else if (!single.TryAdd(name, value))
            {
                error = $"Option '{name}' was given more than once.";
                return false;
            }
        }

        return TryBuild(runs, single, out options, out error);
    }

    private static bool TryBuild(List<string> runs, Dictionary<string, string> single, out ScoreOptions? options, out string? error)
    {
        options = null;
        if (runs.Count == 0)
        {
            error = "Option '--run' is required: the run id to score, e.g. --run 20261022T193000Z-cpu.";
            return false;
        }

        int? budget = null;
        if (single.TryGetValue("--budget-seconds", out string? budgetText))
        {
            if (!int.TryParse(budgetText, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || seconds is < MinBudgetSeconds or > MaxBudgetSeconds)
            {
                error = $"Option '--budget-seconds' needs a whole number from {MinBudgetSeconds} to {MaxBudgetSeconds}.";
                return false;
            }

            budget = seconds;
        }

        options = new ScoreOptions(
            runs,
            Path.GetFullPath(single.GetValueOrDefault("--root") ?? RenderOptionsParser.DefaultRoot()),
            Path.GetFullPath(single.GetValueOrDefault("--engines") ?? Path.Combine(AppContext.BaseDirectory, "engines")),
            single.GetValueOrDefault("--out"),
            budget);
        error = null;
        return true;
    }
}
