// STORY-483 — One version, read once, shown one way (gh-#9 + gh-#868 · SPEC F211.1, F211.7 · PLAN T590)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.
// Reads what SHIPS: the production assemblies' IL/source, not a list of file names.

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureOneVersionLaw
{
    const string Pending = "pending: T590 — one-reader law (STORY-483)";

    public sealed class ScenarioOneReader
    {
        // Given: every production assembly's types that read AssemblyInformationalVersionAttribute

        /// <summary>AC9 — the only reader is the IAppVersion provider</summary>
        [Fact(Skip = Pending)]
        public void OnlyTheProviderReads() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheOldReadersAreGone
    {
        // Given: every production assembly's type names

        /// <summary>AC10 — no HostVersion</summary>
        [Fact(Skip = Pending)]
        public void NoHostVersion() => Assert.Fail(Pending);

        /// <summary>AC10 — no AdAppVersion</summary>
        [Fact(Skip = Pending)]
        public void NoAdAppVersion() => Assert.Fail(Pending);
    }

    public sealed class ScenarioNoContractChange
    {
        // Given: PublicSurface.Of(GenWave.Abstractions) vs the published-package fixture

        /// <summary>AC11 — the diff is empty (F211.7)</summary>
        [Fact(Skip = Pending)]
        public void AbstractionsSurfaceUnchanged() => Assert.Fail(Pending);
    }
}
