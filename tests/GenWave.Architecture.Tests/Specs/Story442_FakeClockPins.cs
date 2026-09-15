// STORY-442 — Time-budget facts run on a fake clock (gh-#723 · SPEC F181.3 · PLAN T482)
//
// AC1/AC2 as source pins: no hand-rolled FakeTimeProvider class remains under tests/, and the three
// projects that used one (Orchestration, Tts, Ads) reference Microsoft.Extensions.TimeProvider.Testing
// instead. AC3–AC6 live in GenWave.Orchestration.Tests / GenWave.Ads.Tests (Story442_*.cs there).
//
// RED at plan time: two hand-rolled fakes exist; Orchestration.Tests has no package reference.

using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureFakeClockPins
{
    const string Pending = "pending: T482 — Microsoft's FakeTimeProvider replaces the hand-rolled ones (STORY-442)";

    static string TestsRoot => Path.Combine(SolutionLocator.Root(), "tests");

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioNoHandRolledFakeClockRemains
    {
        readonly string[] hits = Directory.EnumerateFiles(TestsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("class FakeTimeProvider", StringComparison.Ordinal))
            .ToArray();

        [Fact(Skip = Pending)]
        public void ZeroFilesDeclareOne() => Assert.Empty(hits);
    }

    public sealed class ScenarioThePackageIsReferencedNotVendored
    {
        static string Csproj(string project) =>
            File.ReadAllText(Path.Combine(TestsRoot, project, project + ".csproj"));

        [Fact(Skip = Pending)]
        public void OrchestrationTestsReferencesIt() =>
            Assert.Contains("Microsoft.Extensions.TimeProvider.Testing", Csproj("GenWave.Orchestration.Tests"), StringComparison.Ordinal);

        [Fact]
        public void TtsTestsReferencesIt() =>
            Assert.Contains("Microsoft.Extensions.TimeProvider.Testing", Csproj("GenWave.Tts.Tests"), StringComparison.Ordinal);

        [Fact]
        public void AdsTestsReferencesIt() =>
            Assert.Contains("Microsoft.Extensions.TimeProvider.Testing", Csproj("GenWave.Ads.Tests"), StringComparison.Ordinal);
    }
}
