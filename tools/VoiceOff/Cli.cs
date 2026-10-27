using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff;

/// <summary>Verb dispatch for the Voice Off tool. Output streams are injected so tests run it in-process.</summary>
internal static class Cli
{
    internal static readonly IReadOnlyList<VerbHelp> Verbs =
    [
        new("render", "Render the test scripts with each voice engine.", "voiceoff render --tier cpu [--engine <slug>] [--root <dir>] [--engines <dir>] [--gauntlet <dir>]", "voiceoff render --tier cpu --dry-run"),
        new("serve", "Serve the engine cards on a local web page for ranking.", "voiceoff serve --run <run-id> --port 8765"),
        new("score", "Write the ranking of a run to RESULTS.md.", "voiceoff score --run <run-id> [--out <file>]"),
    ];

    internal const int Ok = 0;
    internal const int NotBuilt = 1;
    internal const int UsageError = 2;

    /// <summary>The synchronous entry for callers with no async context: every verb with the real services, never cancelled.</summary>
    internal static int Run(string[] args, TextWriter output, TextWriter error) =>
        Run(args, output, error, RenderServices.Default());

    /// <summary>Like <see cref="RunAsync"/>, blocking until the verb finishes; for in-process callers (specs) that have no async context.</summary>
    internal static int Run(string[] args, TextWriter output, TextWriter error, RenderServices services, CancellationToken ct = default) =>
        RunAsync(args, output, error, services, ct).GetAwaiter().GetResult();

    internal static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, RenderServices services, CancellationToken ct)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteHelp(output);
            return Ok;
        }

        string verb = args[0];
        if (args.Length > 1 && IsHelpFlag(args[1]) && Verbs.FirstOrDefault(v => string.Equals(v.Name, verb, StringComparison.Ordinal)) is { } known)
        {
            WriteVerbHelp(output, known);
            return Ok;
        }

        if (string.Equals(verb, "render", StringComparison.Ordinal))
        {
            return await RenderCommand.RunAsync(args[1..], output, error, services, ct);
        }

        if (Verbs.Any(v => string.Equals(v.Name, verb, StringComparison.Ordinal)))
        {
            error.WriteLine($"The {verb} verb is not built yet.");
            return NotBuilt;
        }

        error.WriteLine($"Unknown verb '{verb}'. Run 'voiceoff --help' to see the verbs.");
        return UsageError;
    }

    private static bool IsHelp(string arg) =>
        IsHelpFlag(arg) || arg is "help";

    private static bool IsHelpFlag(string arg) =>
        arg is "--help" or "-h";

    private static void WriteVerbHelp(TextWriter output, VerbHelp verb)
    {
        output.WriteLine(verb.Summary);
        output.WriteLine($"Example: {verb.Example}");
        if (verb.SecondExample is not null)
        {
            output.WriteLine($"Example: {verb.SecondExample}");
        }
    }

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
            if (verb.SecondExample is not null)
            {
                output.WriteLine($"          Example: {verb.SecondExample}");
            }
        }
    }
}
