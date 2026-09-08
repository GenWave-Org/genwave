// STORY-412 — A spot belongs to a sponsor and snapshots the name (SPEC F171.7 · PLAN T436)

namespace GenWave.Host.Tests.Specs;

public static class FeatureASpotBelongsToASponsorAndSnapshotsTheName
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioSponsorNameIsStampedAtCreation
    {
        [Fact]
        public void PostIs201()
            => Assert.Fail("pending: T436 POST /api/ads sponsorId — AC2");

        [Fact]
        public void SponsorNameEqualsTheSponsorsNameAtCreation()
            => Assert.Fail("pending: T436 sponsor_name snapshot — AC2");

        [Fact]
        public void TheResponseCarriesTheSponsorObject()
            => Assert.Fail("pending: T436 response sponsor object — AC2");
    }

    public sealed class ScenarioSnapshotRefreshesOnASponsorChange
    {
        [Fact]
        public void PatchingSponsorIdRefreshesSponsorName()
            => Assert.Fail("pending: T436 PATCH /api/ads/{id} sponsorId → sponsor_name — AC3");
    }

    public sealed class ScenarioFilterBySponsorId
    {
        [Fact]
        public void GetAdsFilteredReturnsExactlyThatSponsorsSpots()
            => Assert.Fail("pending: T436 GET /api/ads?sponsorId= — AC6");
    }

    public sealed class ScenarioTheWordBrandAppearsInNoField
    {
        [Fact]
        public void NoRequestOrResponsePropertyIsNamedBrand()
            => Assert.Fail("pending: T436 contract scan /api/ads, /api/ad-briefs, /api/sponsors — AC7");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingInvalidInput
    {
        [Fact]
        public void MissingSponsorIdIs400SponsorRequired()
            => Assert.Fail("pending: T436 POST sponsor_required — AC1");

        [Fact]
        public void APausedSponsorIs409SponsorPaused()
            => Assert.Fail("pending: T436 POST sponsor_paused — AC4");

        [Fact]
        public void AnUnknownSponsorIs404SponsorNotFound()
            => Assert.Fail("pending: T436 POST sponsor_not_found — AC5");
    }
}
