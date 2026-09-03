// STORY-385 — PluginLoader's own key-format regex stays byte-identical to ContextPipeline's (F156.6 · T392)
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// <c>GenWave.Plugins.PluginLoader</c> deliberately carries its OWN copy of
/// <c>GenWave.Context.ContextPipeline</c>'s key-format regex rather than referencing
/// <c>GenWave.Context</c> at all (that project's own csproj reference-rationale comment;
/// <c>PluginLoader</c>'s own class remarks) — a plugin's <c>IContextProvider.Key</c> must be
/// pre-validated BEFORE it can ever reach <c>ContextPipeline</c>'s own fail-fast constructor
/// (F156.6), so the loader needs its own copy of the identical rule, not a dependency on the type
/// it exists to protect. That duplication has no compiler tie between the two copies — nothing stops
/// one from drifting from the other silently the next time either file changes (T392 review finding
/// 6).
///
/// <para>
/// Pinned by SOURCE TEXT, not by exercising either compiled regex against a shared probe corpus — the
/// <c>Story291_ConventionLaws.cs</c> "Program.cs registers exactly three HttpClients" precedent for
/// reading a source file's own text directly, rather than its compiled behavior. Two DIFFERENT regex
/// patterns that happen to accept/reject the same probe corpus would still pass a behavioral test (e.g.
/// one written with an explicit character class, the other equivalent via a POSIX class or an anchor
/// variant) — only a literal string comparison between the two source files' own
/// <c>[GeneratedRegex("...")]</c> attribute arguments actually proves "the identical rule," not merely
/// "a rule that happens to agree on this corpus."
/// </para>
/// </summary>
public static class FeaturePluginKeyPatternMatchesContextPipeline
{
    [Fact]
    public static void PluginLoaderCarriesTheIdenticalKeyPatternLiteralAsContextPipeline()
    {
        var pluginLoaderPattern = ExtractGeneratedRegexArgument(
            Path.Combine(SolutionLocator.Root(), "src", "GenWave.Plugins", "PluginLoader.cs"));
        var contextPipelinePattern = ExtractGeneratedRegexArgument(
            Path.Combine(SolutionLocator.Root(), "src", "GenWave.Context", "ContextPipeline.cs"));

        Assert.Equal(contextPipelinePattern, pluginLoaderPattern);
    }

    /// <summary>Pulls the literal text between a source file's own <c>[GeneratedRegex(</c> marker and
    /// its closing <c>)]</c> — a plain substring slice, not a Regex probe of the source text itself
    /// (this feature's own class remarks on why source TEXT, not behavior, is what proves identity
    /// here; using Regex to find a Regex literal would also fight its own escaping rules for no
    /// benefit). Throws when a file carries none, or more than one, <c>[GeneratedRegex(</c> marker —
    /// either shape means this helper's own single-match assumption no longer holds for that file, a
    /// louder failure than silently comparing the wrong occurrence.</summary>
    static string ExtractGeneratedRegexArgument(string sourceFilePath)
    {
        const string marker = "[GeneratedRegex(";
        var sourceText = File.ReadAllText(sourceFilePath);

        var start = sourceText.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"\"{sourceFilePath}\" carries no [GeneratedRegex(...)] attribute.");

        if (sourceText.IndexOf(marker, start + marker.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"\"{sourceFilePath}\" carries more than one [GeneratedRegex(...)] attribute.");

        var argumentStart = start + marker.Length;
        var argumentEnd = sourceText.IndexOf(")]", argumentStart, StringComparison.Ordinal);
        if (argumentEnd < 0)
            throw new InvalidOperationException($"\"{sourceFilePath}\"'s [GeneratedRegex(...)] attribute has no closing \")]\".");

        return sourceText[argumentStart..argumentEnd];
    }
}
