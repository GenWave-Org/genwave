// STORY-287 — Save-as-own (SPEC F104.13 · PLAN T207)
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureSaveAsOwn
{
    public sealed class ScenarioSaveWritesAnAuthoredTheme
    {
        [Fact(Skip = "pending T207 (STORY-287 AC1)")]
        public void ASavedRemixLandsInStationThemeWithNullProvenance() { }

        [Fact(Skip = "pending T207 (STORY-287 AC1)")]
        public void TheSavedThemeIsImmediatelySelectableAndResolvable() { }
    }

    public sealed class ScenarioTheBaseThemeIsUntouched
    {
        [Fact(Skip = "pending T207 (STORY-287 AC2)")]
        public void TheBaseThemeIsByteIdenticalAfterTheSave() { }
    }

    public sealed class ScenarioSavesPassTheSameLaw
    {
        [Fact(Skip = "pending T207 (STORY-287 AC3)")]
        public void ALawViolatingSaveRefusesWithTheImportRoutesExactCopy() { }

        [Fact(Skip = "pending T207 (STORY-287 AC3)")]
        public void AShippedSlugCollisionRefuses409() { }
    }
}
