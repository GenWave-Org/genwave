// STORY-501 — Nothing ships, and no slug in code (gh-#614 · SPEC F214.16, F214.17 · PLAN T619)
//
// BDD specification — xUnit. AC3–AC5 live in GenWave.VoiceOff.Tests.
//
// LIVE at T619: law L12 (VoiceOffSlugFence) — AC1 (no slug in tools/VoiceOff code), AC2 (no
// Dockerfile or compose file mentions VoiceOff) and AC6 (a planted slug fails naming file and slug),
// plus a scratch-tree pin that the excluded engines/ directory is never scanned.

using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureNothingShipsAndNoSlugInCode
{
    static string VoiceOffRoot => Path.Combine(SolutionLocator.Root(), "tools", "VoiceOff");

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the slug fence holds
    public sealed class ScenarioTheSlugFenceHolds
    {
        readonly IReadOnlyList<LawViolation> violations;

        public ScenarioTheSlugFenceHolds()
        {
            var slugs = VoiceOffSlugFence.Slugs(Path.Combine(VoiceOffRoot, "engines"));
            violations = VoiceOffSlugFence.Scan(VoiceOffRoot, slugs);
        }

        [Fact]
        public void NoSlugAppearsAsAWholeWord() => Assert.True(violations.Count == 0, VoiceOffSlugFence.Format(violations));
    }

    // AC2 — no image builds the tool
    public sealed class ScenarioNoImageBuildsTheTool
    {
        readonly IReadOnlyList<LawViolation> violations = VoiceOffSlugFence.ScanImages(SolutionLocator.Root());

        [Fact]
        public void NoneMentionsVoiceOff() => Assert.True(violations.Count == 0, VoiceOffSlugFence.Format(violations));
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC6 — the fence bites
    public sealed class ScenarioTheFenceBites : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("story501-fence-").FullName;
        readonly string message;

        public ScenarioTheFenceBites()
        {
            File.WriteAllText(Path.Combine(root, "Planted.cs"), "// uses acme here\n");
            message = VoiceOffSlugFence.Format(VoiceOffSlugFence.Scan(root, ["acme"]));
        }

        public void Dispose() => Directory.Delete(root, true);

        [Fact]
        public void ItFailsNamingTheFile() => Assert.Contains("Planted.cs", message);

        [Fact]
        public void ItFailsNamingTheSlug() => Assert.Contains("'acme'", message);
    }

    public sealed class ScenarioAnExcludedDirectoryIsNotScanned : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("story501-excluded-").FullName;
        readonly IReadOnlyList<LawViolation> violations;

        public ScenarioAnExcludedDirectoryIsNotScanned()
        {
            Directory.CreateDirectory(Path.Combine(root, "engines"));
            File.WriteAllText(Path.Combine(root, "engines", "Planted.cs"), "// acme\n");
            violations = VoiceOffSlugFence.Scan(root, ["acme"]);
        }

        public void Dispose() => Directory.Delete(root, true);

        [Fact]
        public void NothingIsReported() => Assert.Empty(violations);
    }
}
