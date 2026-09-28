// STORY-483 — One version, read once, shown one way (gh-#9 + gh-#868 · SPEC F211.1–F211.2 · PLAN T588)
//
// BDD specification — xUnit. Seam: the IAppVersion parser (pure). The entry-point proofs live in
// Host Story483_OneVersion.

using GenWave.Core.Abstractions;

namespace GenWave.Core.Tests.Specs;

public static class FeatureAppVersionParse
{
    public sealed class ScenarioAStampWithABuildSuffix
    {
        readonly IAppVersion version;

        public ScenarioAStampWithABuildSuffix()
        {
            // Given: InformationalVersion "5.13.2+abc1234" parsed once
            version = AppVersion.From("5.13.2+abc1234");
        }

        /// <summary>AC1 — Display is "v5.13.2"</summary>
        [Fact]
        public void DisplaysVPlusSemver() => Assert.Equal("v5.13.2", version.Display);

        /// <summary>AC2 — Semver is "5.13.2"</summary>
        [Fact]
        public void KeepsTheSemver() => Assert.Equal("5.13.2", version.Semver);

        /// <summary>AC3 — Build is "5.13.2+abc1234"</summary>
        [Fact]
        public void BuildCarriesTheSha() => Assert.Equal("5.13.2+abc1234", version.Build);
    }

    public sealed class ScenarioATagShapedStamp
    {
        readonly IAppVersion version;

        public ScenarioATagShapedStamp()
        {
            // Given: InformationalVersion "v5.13.3" (the release build's GW_VERSION) parsed once
            version = AppVersion.From("v5.13.3");
        }

        /// <summary>AC4 — Display is "v5.13.3", the v not doubled</summary>
        [Fact]
        public void DoesNotDoubleTheV() => Assert.Equal("v5.13.3", version.Display);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioABlankStamp
    {
        readonly IAppVersion version;

        public ScenarioABlankStamp()
        {
            // Given: InformationalVersion "" parsed once
            version = AppVersion.From("");
        }

        /// <summary>AC12 — Display is "unknown"</summary>
        [Fact]
        public void IsUnknown() => Assert.Equal("unknown", version.Display);
    }

    public sealed class ScenarioNoStamp
    {
        readonly IAppVersion version;

        public ScenarioNoStamp()
        {
            // Given: InformationalVersion null parsed once
            version = AppVersion.From(null);
        }

        /// <summary>AC12 — Display is "unknown"</summary>
        [Fact]
        public void IsUnknown() => Assert.Equal("unknown", version.Display);
    }

    public sealed class ScenarioAGarbageStamp
    {
        readonly IAppVersion version;

        public ScenarioAGarbageStamp()
        {
            // Given: InformationalVersion "garbage" parsed once
            version = AppVersion.From("garbage");
        }

        /// <summary>AC12 — Display is "unknown"</summary>
        [Fact]
        public void IsUnknown() => Assert.Equal("unknown", version.Display);
    }

    public sealed class ScenarioAVPrefixedGarbageStamp
    {
        readonly IAppVersion version;

        public ScenarioAVPrefixedGarbageStamp()
        {
            // Given: InformationalVersion "vgarbage" parsed once
            version = AppVersion.From("vgarbage");
        }

        /// <summary>AC12 — Build is the trimmed input VERBATIM, not re-stripped of its leading v</summary>
        [Fact]
        public void BuildKeepsTheRawStampVerbatim() => Assert.Equal("vgarbage", version.Build);
    }

    public sealed class ScenarioJustTheLetterV
    {
        readonly IAppVersion version;

        public ScenarioJustTheLetterV()
        {
            // Given: InformationalVersion "v" parsed once
            version = AppVersion.From("v");
        }

        /// <summary>AC12 — Build is the trimmed input ("v"), never emptied by stripping</summary>
        [Fact]
        public void BuildIsNeverEmptied() => Assert.Equal("v", version.Build);
    }
}
