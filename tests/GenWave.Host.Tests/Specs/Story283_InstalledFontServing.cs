// STORY-283 — Installed faces serve at /fonts (SPEC F104.6, F104.8 · PLAN T200)
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureInstalledFontServing
{
    public sealed class ScenarioTheClosedSetWidens
    {
        [Fact(Skip = "pending T200 (STORY-283 AC1)")]
        public void AnInstalledFaceServesWithWoff2ContentType() { }

        [Fact(Skip = "pending T200 (STORY-283 AC1)")]
        public void AnInstalledFaceCarriesTheVendoredCachingPosture() { }
    }

    public sealed class ScenarioInstalledFacesSurviveOutages
    {
        [Fact(Skip = "pending T200 (STORY-283 AC2)")]
        public void ALoadedFaceStillServesWithTheCatalogUnreachable() { }

        [Fact(Skip = "pending T200 (STORY-283 AC2)")]
        public void TheEmbeddedThemeFloorIsUntouchedByPackMachinery() { }
    }

    public sealed class ScenarioTheSetStaysClosedAndNonEnumerable
    {
        [Fact(Skip = "pending T200 (STORY-283 AC3)")]
        public void AnUnknownFileStill404s() { }

        [Fact(Skip = "pending T200 (STORY-283 AC3)")]
        public void NoRouteListsTheFontSetOnAnySurface() { }
    }
}
