using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace GenWave.IconPackAuthor;

/// <summary>
/// This project's own CI-runnable smoke test (PLAN T305's own "at minimum the 4 sample conversions
/// run as part of your verification" instruction, made a re-runnable command rather than a one-off
/// manual check) — <c>dotnet run --project tools/IconPackAuthor -- self-test</c>. Drives
/// <see cref="PackAuthoringPipeline"/> — the exact same entry point <c>author</c> uses — against the
/// 4 hand-authored SVGs under <c>testdata/</c>: an outline glyph (paths + a circle, exercising arc-
/// flag scaling and H/V commands), a filled glyph (fill=currentColor, exercising Z-close and relative
/// arcs), a <c>&lt;g&gt;</c>-wrapped glyph (must fail — grouping is outside the whitelist), and a
/// literal-hex-stroke glyph (must fail — colors are inexpressible by schema). The two failure
/// scenarios each pair the bad glyph with the ALREADY-PROVEN-GOOD outline glyph, to demonstrate the
/// pipeline's own whole-run-reject posture: a good glyph never ships alone just because its sibling in
/// the same mapping was bad.
///
/// No test framework here deliberately (PLAN T305's own "state your choice"): this project is a
/// console <c>Exe</c>, not a library under test — a self-test mode mirrors <c>tools/check-seam-index.sh</c>'s
/// own "generate fresh, compare, fail loud" idiom for a tool this codebase already treats as
/// CI-adjacent rather than app-adjacent. The pure logic it drives (<see cref="PathDataTransform"/>,
/// <see cref="SvgGlyphConverter"/>, <see cref="PackAuthoringPipeline"/>) has no framework dependency of
/// its own, so nothing here stops a future xUnit project from referencing this one and driving the
/// same public entry points directly, if that becomes worth the extra project.
/// </summary>
public static class SelfTest
{
    /// <summary>One scenario: which fixture mapping to run against the shared <c>testdata/</c> source
    /// directory, and whether the run is expected to succeed.</summary>
    static readonly (string Name, string MappingFile, bool ExpectSuccess)[] Scenarios =
    [
        ("outline pack (happy — paths + a circle, arc-flag + H/V scaling)", "mapping-outline.json", true),
        ("filled pack (happy — fill=currentColor, relative arcs + Z-close)", "mapping-filled.json", true),
        ("<g>-wrapped glyph (must fail — grouping is outside the whitelist)", "mapping-group-wrapper-fails.json", false),
        ("literal-hex-stroke glyph (must fail — colors are inexpressible)", "mapping-literal-color-fails.json", false),
        ("epsilon pack (happy — sub-1e-4 coordinates in both 'd' and 'points' round to clean, PointsText-legal output)", "mapping-epsilon.json", true),
    ];

    public static int Run()
    {
        var dir = TestDataDirectory();
        var allPassed = true;

        foreach (var (name, mappingFile, expectSuccess) in Scenarios)
        {
            Console.WriteLine($"--- {name} ---");
            var mapping = NameMapping.Load(Path.Combine(dir, mappingFile));
            var outcome = PackAuthoringPipeline.Run(dir, mapping, fillOverride: null, strokeWidthOverride: null);

            bool passed;
            switch (outcome)
            {
                case PackAuthoringOutcome.Success success:
                    Console.WriteLine(
                        $"  icons: {success.Definition.Icons.Count}, style=fill:{success.Definition.Style.Fill} " +
                        $"strokeWidth:{success.Definition.Style.StrokeWidth:0.###}");
                    Console.WriteLine($"  canonical JSON: {success.CanonicalJson}");
                    passed = expectSuccess;
                    break;

                case PackAuthoringOutcome.Failure failure:
                    foreach (var reason in failure.Reasons)
                        Console.WriteLine($"  - {reason}");
                    passed = !expectSuccess;
                    break;

                default:
                    throw new UnreachableException($"Unhandled {nameof(PackAuthoringOutcome)} case.");
            }

            Console.WriteLine(passed ? "PASS" : "FAIL");
            Console.WriteLine();
            allPassed &= passed;
        }

        Console.WriteLine(allPassed ? "self-test: ALL SCENARIOS PASSED" : "self-test: SOME SCENARIOS FAILED");
        return allPassed ? 0 : 1;
    }

    /// <summary>Locates <c>testdata/</c> relative to THIS source file's own compile-time path — robust
    /// regardless of the process's working directory or build configuration, without needing a
    /// repo-root walk (mirrors <c>tools/SeamIndexGenerator/RepoRoot.cs</c>'s own goal, by a simpler
    /// route available here since the fixtures live inside this very project).
    ///
    /// <para>
    /// <b>SAME-WORKSPACE CONSTRAINT</b> (mirrors <c>tools/check-seam-index.sh</c>'s own CRITICAL
    /// same-job note for <c>tools/SeamIndexGenerator</c>): <see cref="CallerFilePathAttribute"/> bakes
    /// the BUILD MACHINE's absolute source path into the compiled assembly at compile time. <c>self-
    /// test</c> therefore only finds <c>testdata/</c> when run from the SAME checkout that built it —
    /// a binary copied elsewhere, or restored from a different job/workspace, resolves to a path that
    /// no longer exists. This is deliberately the ONLY resolution mechanism for these fixtures (see
    /// <c>IconPackAuthor.csproj</c>'s own remarks on why a build-output copy was removed rather than
    /// kept as a second route): the constraint lives here, in one place, instead of two mechanisms that
    /// could each resolve to a different set of files.
    /// </para>
    /// </summary>
    static string TestDataDirectory([CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile) ?? ".", "testdata");
}
