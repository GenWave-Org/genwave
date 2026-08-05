// STORY-285 — The widened font law (SPEC F104.9, F104.10 · PLAN T205)
// AC3 (catalog CI stays curated-only) is pinned in the genwave-catalog repo's own idiom.
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureWidenedFontLaw
{
    public sealed class ScenarioTheUnionAdmitsInstalledFaces
    {
        [Fact(Skip = "pending T205 (STORY-285 AC1)")]
        public void AThemeReferencingAnInstalledPackFaceImports200() { }

        [Fact(Skip = "pending T205 (STORY-285 AC1)")]
        public void ThePerThemeCeilingSumsRecordedBytesAcrossVendoredAndInstalled() { }
    }

    public sealed class ScenarioAMissingPackIsNamed
    {
        [Fact(Skip = "pending T205 (STORY-285 AC2)")]
        public void AnUninstalledFaceRefuses400NamingTheFace() { }

        [Fact(Skip = "pending T205 (STORY-285 AC2)")]
        public void TheRefusalNamesTheProvidingPackSlugWhenTheIndexKnowsOne() { }
    }
}
