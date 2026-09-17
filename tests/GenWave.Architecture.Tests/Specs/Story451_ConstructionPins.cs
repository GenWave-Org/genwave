// STORY-451 — One construction path — the pins (gh-#401 · SPEC F184.3–F184.5 · PLAN T510, T513, T514)
//
// BDD specification — xUnit. AC4 scans src/ and tests/ for `new Orchestrator(`; AC5 reads Host.Tests' csproj and file list; AC8 reflects the
// TestSupport assembly for [Fact] methods.
//
// RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body — remove the Skip only in the
// task that makes it green. The builder comments name the arrange each scenario needs.

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureConstructionPins
{
    const string Pending = "pending: T514 — two `new Orchestrator(` sites; Host.Tests rides TestSupport (STORY-451)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheTextScanForConstruction
    {
        // Given: every .cs under src/ and tests/ scanned for `new Orchestrator(`

        /// <summary>AC4 — the builder is one site</summary>
        [Fact(Skip = Pending)]
        public void HitsOrchestratorBuilder() => Assert.Fail(Pending);

        /// <summary>AC4 — AddOrchestration is the other site</summary>
        [Fact(Skip = Pending)]
        public void HitsTheServiceCollectionExtensions() => Assert.Fail(Pending);

        /// <summary>AC4 — exactly two files hit</summary>
        [Fact(Skip = Pending)]
        public void HitsNothingElse() => Assert.Fail(Pending);
    }

    public sealed class ScenarioHostTestsAfterTheMove
    {
        // Given: GenWave.Host.Tests.csproj and its Fakes folder

        /// <summary>AC5 — the ProjectReference is present</summary>
        [Fact(Skip = Pending)]
        public void ReferencesTestSupport() => Assert.Fail(Pending);

        /// <summary>AC5 — the duplicate fake is deleted</summary>
        [Fact(Skip = Pending)]
        public void NoLongerCarriesFakeRenderBudgetProvider() => Assert.Fail(Pending);

        /// <summary>AC5 — the duplicate fake is deleted</summary>
        [Fact(Skip = Pending)]
        public void NoLongerCarriesFakeBoundaryBiasProvider() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheSupportAssembly
    {
        // Given: GenWave.TestSupport reflected

        /// <summary>AC8 — the project carries fakes, not facts</summary>
        [Fact(Skip = Pending)]
        public void HasNoFactMethods() => Assert.Fail(Pending);
    }
}
