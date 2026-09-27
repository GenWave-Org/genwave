// STORY-483 — One version, read once, shown one way (gh-#9 + gh-#868 · SPEC F211.1–F211.2 · PLAN T588)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.
// Seam: the IAppVersion parser (pure). The entry-point proofs live in Host Story483_OneVersion.

namespace GenWave.Core.Tests.Specs;

public static class FeatureAppVersionParse
{
    const string Pending = "pending: T588 — IAppVersion provider (STORY-483)";

    public sealed class ScenarioAStampWithABuildSuffix
    {
        // Given: InformationalVersion "5.13.2+abc1234" parsed once

        /// <summary>AC1 — Display is "v5.13.2"</summary>
        [Fact(Skip = Pending)]
        public void DisplaysVPlusSemver() => Assert.Fail(Pending);

        /// <summary>AC2 — Semver is "5.13.2"</summary>
        [Fact(Skip = Pending)]
        public void KeepsTheSemver() => Assert.Fail(Pending);

        /// <summary>AC3 — Build is "5.13.2+abc1234"</summary>
        [Fact(Skip = Pending)]
        public void BuildCarriesTheSha() => Assert.Fail(Pending);
    }

    public sealed class ScenarioATagShapedStamp
    {
        // Given: InformationalVersion "v5.13.3" (the release build's GW_VERSION) parsed once

        /// <summary>AC4 — Display is "v5.13.3", the v not doubled</summary>
        [Fact(Skip = Pending)]
        public void DoesNotDoubleTheV() => Assert.Fail(Pending);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioABlankStamp
    {
        // Given: InformationalVersion "" parsed once

        /// <summary>AC12 — Display is "unknown"</summary>
        [Fact(Skip = Pending)]
        public void IsUnknown() => Assert.Fail(Pending);
    }

    public sealed class ScenarioNoStamp
    {
        // Given: InformationalVersion null parsed once

        /// <summary>AC12 — Display is "unknown"</summary>
        [Fact(Skip = Pending)]
        public void IsUnknown() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAGarbageStamp
    {
        // Given: InformationalVersion "garbage" parsed once

        /// <summary>AC12 — Display is "unknown"</summary>
        [Fact(Skip = Pending)]
        public void IsUnknown() => Assert.Fail(Pending);
    }
}
