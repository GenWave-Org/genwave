using System.Text.RegularExpressions;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// The source-text half of L3's <c>Program.cs</c> pin (STORY-291 review): <see cref="HttpClientMetadataScan"/>
/// sees THAT the composition root builds a handler, not WHAT boolean it was built with, nor how many
/// distinct <c>AddHttpClient</c> registrations exist — a metadata scan already proves construction
/// shape structurally; only reading the file's own text can catch a 4th registration, an
/// <c>AllowAutoRedirect = true</c> regression, or a bypass-the-DI-container raw client neither the
/// metadata scan nor a bare type-count assertion would notice.
///
/// One regex literal, one place (PLAN T406 review MED-4): <c>Specs.FeatureConventionLaws.ScenarioL3ProgramCompositionRoot</c>
/// (Story291_ConventionLaws.cs, L3's own pin — count + <c>AllowAutoRedirect</c> + the no-hand-rolled-client
/// guard) and <c>Specs.FeatureShipHonestPins.ScenarioTheLawsKnowTheNewProjects.TheAddHttpClientPinStillReadsThree</c>
/// (Story394_ShipHonestPins.cs, STORY-394's own independent re-pin that neither the plugin loader nor
/// the ads lane grew a 4th seam) both call this one detector rather than each keeping its own copy of
/// the pattern to drift apart.
/// </summary>
internal static class ProgramHttpClientRegistrations
{
    static readonly Regex Pattern = new(@"\bAddHttpClient(<[^>]+>)?\(", RegexOptions.Compiled);

    /// <summary>Reads <c>src/GenWave.Host/Program.cs</c> verbatim off disk.</summary>
    public static string ReadProgramText() =>
        File.ReadAllText(Path.Combine(SolutionLocator.Root(), "src", "GenWave.Host", "Program.cs"));

    /// <summary>Counts every <c>AddHttpClient(...)</c>/<c>AddHttpClient&lt;T&gt;(...)</c> call site in
    /// <paramref name="programText"/> — a plain call-site count, not a claim about what each
    /// registration does (that's <see cref="HttpClientMetadataScan"/>'s job).</summary>
    public static int CountAddHttpClientRegistrations(string programText) =>
        Pattern.Matches(programText).Count;
}
