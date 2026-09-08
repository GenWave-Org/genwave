// STORY-415 — Installing an ad pack creates its sponsors (SPEC F172.3 · PLAN T437)

namespace GenWave.Host.Tests.Specs;

public static class FeatureInstallingAnAdPackCreatesItsSponsors
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioInstallUpsertsSponsorsThenBriefs
    {
        [Fact]
        public void InstallIs200()
            => Assert.Fail("pending: T437 POST /api/ad-packs/{slug}/install — AC1");

        [Fact]
        public void ThirtyPackOwnedSponsorsExist()
            => Assert.Fail("pending: T437 station.sponsor pack_slug rows — AC1");

        [Fact]
        public void EveryBriefPointsAtTheSponsorNamedInTheManifest()
            => Assert.Fail("pending: T437 brief.sponsor_id mapping — AC1");
    }

    public sealed class ScenarioTheCatalogManifestStillSaysBrand
    {
        [Fact]
        public void TheAdPackManifestKeepsItsBrandField()
            => Assert.Fail("pending: T437 catalog schema unchanged — AC2");
    }

    public sealed class ScenarioReinstallIsIdempotent
    {
        [Fact]
        public void SponsorRowCountIsUnchanged()
            => Assert.Fail("pending: T437 reinstall by (pack_slug, name_key) — AC3");

        [Fact]
        public void EveryBriefKeepsItsSponsorId()
            => Assert.Fail("pending: T437 reinstall keeps FKs — AC3");
    }
}
