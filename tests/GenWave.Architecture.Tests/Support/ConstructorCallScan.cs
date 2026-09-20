namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// STORY-451's own text scan for the Orchestrator's construction call, lifted here at STORY-460
/// (PLAN T537) so Story451_ConstructionPins.cs's three-fact proof and Story460_TheOrchestratorAfter.cs's
/// own set-equality fact both read ONE scan instead of two independently maintained copies that
/// could quietly drift apart. The literal is split (<c>"new " + "Orchestrator("</c>) so this
/// file's own source text never spells it out contiguously and self-matches the scan.
/// </summary>
internal static class ConstructorCallScan
{
    const string ConstructorCallLiteral = "new " + "Orchestrator(";

    /// <summary>Every <c>.cs</c> file under <c>src/</c> and <c>tests/</c> (<c>bin</c>/<c>obj</c>
    /// excluded) whose text contains the Orchestrator's own construction call. A static-readonly
    /// property, not a method — computed once and shared by every caller (round 2, PLAN T537 review
    /// note 4): Story451_ConstructionPins.cs and Story460_TheOrchestratorAfter.cs both read this
    /// SAME cached list, rather than each `File.ReadAllText`-ing the whole src/+tests/ tree on every
    /// call.</summary>
    public static IReadOnlyList<string> Hits { get; } = ComputeHits();

    static IReadOnlyList<string> ComputeHits() =>
        new[] { "src", "tests" }
            .SelectMany(dir => Directory.EnumerateFiles(
                Path.Combine(SolutionLocator.Root(), dir), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Split('/', '\\').Any(segment => segment is "bin" or "obj"))
            .Where(path => File.ReadAllText(path).Contains(ConstructorCallLiteral, StringComparison.Ordinal))
            .ToArray();
}
