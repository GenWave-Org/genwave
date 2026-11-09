namespace GenWave.PatterBench;

/// <summary>The parsed command line (SPEC F218.27).</summary>
internal sealed record BenchArgs(
    Uri Endpoint,
    IReadOnlyList<string> Models,
    IReadOnlyList<string> Personas,
    int Breaks,
    string OutPath)
{
    internal const int DefaultBreaks = 60;

    internal static readonly IReadOnlyList<string> DefaultPersonas = ["flip", "sal", "tony"];

    internal const string Usage =
        "Usage: PatterBench --endpoint <url> --models <a,b> [--personas flip,sal,tony] [-n 60] [--out <file>]";

    /// <summary>Parses the arguments; on failure <paramref name="problem"/> says what is wrong.</summary>
    internal static bool TryParse(string[] args, DateTimeOffset now, out BenchArgs? parsed, out string problem)
    {
        parsed = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!IsKnownFlag(args[i]) || i + 1 >= args.Length)
            {
                problem = $"Cannot read the argument '{args[i]}'.";
                return false;
            }

            values[args[i]] = args[i + 1];
        }

        if (!values.TryGetValue("--endpoint", out var endpointText)
            || !Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            problem = "--endpoint must be an http or https URL.";
            return false;
        }

        var models = Split(values.GetValueOrDefault("--models"));
        if (models.Count == 0)
        {
            problem = "--models needs at least one model name.";
            return false;
        }

        var personas = values.TryGetValue("--personas", out var personaText) ? Split(personaText) : DefaultPersonas;
        if (personas.Count == 0)
        {
            problem = "--personas needs at least one persona slug.";
            return false;
        }

        var breaks = DefaultBreaks;
        if (values.TryGetValue("-n", out var breaksText)
            && (!int.TryParse(breaksText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out breaks) || breaks < 1))
        {
            problem = "-n must be a whole number of at least 1.";
            return false;
        }

        var outPath = values.GetValueOrDefault("--out")
            ?? Path.Combine("tools", "PatterBench", $"report-{now:yyyyMMdd-HHmm}.md");
        parsed = new BenchArgs(endpoint, models, personas, breaks, outPath);
        problem = string.Empty;
        return true;
    }

    private static bool IsKnownFlag(string arg) =>
        arg is "--endpoint" or "--models" or "--personas" or "-n" or "--out";

    private static List<string> Split(string? text) =>
        (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
