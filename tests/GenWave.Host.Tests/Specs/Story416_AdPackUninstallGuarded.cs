// STORY-416 — Uninstalling an ad pack is guarded (SPEC F172.4 · PLAN T437)

namespace GenWave.Host.Tests.Specs;

public static class FeatureUninstallingAnAdPackIsGuarded
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioCleanUninstallRemovesPackBriefsAndPackSponsors
    {
        [Fact]
        public void UninstallIs204()
            => Assert.Fail("pending: T437 DELETE /api/ad-packs/{slug} — AC1");

        [Fact]
        public void NoPackBriefRemains()
            => Assert.Fail("pending: T437 ad_brief pack_slug rows gone — AC1");

        [Fact]
        public void NoPackSponsorRemains()
            => Assert.Fail("pending: T437 sponsor pack_slug rows gone — AC1");
    }

    public sealed class ScenarioPackSourcedSpotsAreRetired
    {
        [Fact]
        public void TheThreePackSpotsAreRetired()
            => Assert.Fail("pending: T437 uninstall retires source='pack' spots — AC3");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRefuseNamesTheReferencingOwnerWork
    {
        [Fact]
        public void UninstallIs409AdPackInUse()
            => Assert.Fail("pending: T437 DELETE 409 ad_pack_in_use — AC2");

        [Fact]
        public void TheDetailNamesTheOwnerSpotsAndShows()
            => Assert.Fail("pending: T437 409 detail — AC2");

        [Fact]
        public void NothingWasRemoved()
            => Assert.Fail("pending: T437 409 leaves rows — AC2");
    }
}
