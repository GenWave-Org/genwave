// STORY-393 — Ad packs ride the shelf as data (F162.2 · pending T405)

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdPackKind
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheKindParsesAndLists
    {
        [Fact(Skip = "Pending T405 — see docs/PLAN.md")]
        public void AnAdPackEntryValidatesUnderItsKindFolder()
        {
            // entries/ad-packs/<slug>/ + manifest path parity — the font/icon validator arms.
            Assert.Fail("pending T405");
        }

        [Fact(Skip = "Pending T405 — see docs/PLAN.md")]
        public void TheShelfEndpointProjectsThePackWithItsBriefsSummarized()
        {
            Assert.Fail("pending T405");
        }
    }

    public sealed class ScenarioInstallUpsertsBriefs
    {
        [Fact(Skip = "Pending T405 — see docs/PLAN.md")]
        public void InstallingAThreeBriefPackYieldsThreeRows()
        {
            Assert.Fail("pending T405");
        }

        [Fact(Skip = "Pending T405 — see docs/PLAN.md")]
        public void ReinstallUpdatesInPlaceNeverDuplicates()
        {
            // Keyed (pack_slug, brand): install twice, count unchanged, fields updated.
            Assert.Fail("pending T405");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioForwardAndFailClosedCompat
    {
        [Fact(Skip = "Pending T405 — see docs/PLAN.md")]
        public void AnUnknownKindDropsTheEntryAndKeepsTheIndex()
        {
            // The shipped forward-compat pin (CatalogEntryKind precedent) re-pinned over ad-pack.
            Assert.Fail("pending T405");
        }

        [Fact(Skip = "Pending T405 — see docs/PLAN.md")]
        public void AnAbsoluteAssetPathOrSlugMismatchRejectsTheIndexWhole()
        {
            // The standing SSRF posture (F162.2).
            Assert.Fail("pending T405");
        }

        [Fact(Skip = "Pending T405 — see docs/PLAN.md")]
        public void AnInstalledBriefStillFacesTheBrandBlocklist()
        {
            // STORY-393 AC3: a colliding installed brief is refused at generation like any other.
            Assert.Fail("pending T405");
        }
    }
}
