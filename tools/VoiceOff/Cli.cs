namespace GenWave.VoiceOff;

/// <summary>Verb dispatch for the Voice Off tool. Output streams are injected so tests run it in-process.</summary>
internal static class Cli
{
    internal static readonly IReadOnlyList<VerbHelp> Verbs =
    [
        new("render", "Render the test scripts with each voice engine.", "voiceoff render --tier cpu"),
        new("pair", "Build blind A/B pairs from a finished render run.", "voiceoff pair --run <run-id>"),
        new("serve", "Serve the pairs on a local web page for listening.", "voiceoff serve --run <run-id> --port 8765"),
        new("reel", "Join a run's clips into one reel to listen through.", "voiceoff reel --run <run-id>"),
        new("score", "Score the listening results of a run.", "voiceoff score --run <run-id>"),
    ];

    internal const int Ok = 0;
    internal const int NotBuilt = 1;
    internal const int UsageError = 2;

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteHelp(output);
            return Ok;
        }

        string verb = args[0];
        if (Verbs.Any(v => string.Equals(v.Name, verb, StringComparison.Ordinal)))
        {
            error.WriteLine($"The {verb} verb is not built yet.");
            return NotBuilt;
        }

        error.WriteLine($"Unknown verb '{verb}'. Run 'voiceoff --help' to see the verbs.");
        return UsageError;
    }

    private static bool IsHelp(string arg) =>
        arg is "--help" or "-h" or "help";

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("Voice Off: compare voice engines side by side.");
        output.WriteLine();
        output.WriteLine("Usage: voiceoff <verb> [options]");
        output.WriteLine();
        output.WriteLine("Verbs:");
        foreach (VerbHelp verb in Verbs)
        {
            output.WriteLine($"  {verb.Name,-8}{verb.Summary}");
            output.WriteLine($"          Example: {verb.Example}");
        }
    }
}
