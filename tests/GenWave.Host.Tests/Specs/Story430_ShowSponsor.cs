// STORY-430 — A show can name a sponsor (SPEC F175.1–F175.3 · PLAN T449)

namespace GenWave.Host.Tests.Specs;

public static class FeatureAShowCanNameASponsor
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheShowCarriesSponsorIdNullable
    {
        [Fact]
        public void StationShowHasANullableSponsorId()
            => Assert.Fail("pending: T449 db/46 show.sponsor_id — AC1");

        [Fact]
        public void TheFkIsOnDeleteRestrict()
            => Assert.Fail("pending: T449 db/46 FK clause — AC1");
    }

    public sealed class ScenarioTheShowDtoExposesTheSponsorObject
    {
        [Fact]
        public void GetShowCarriesSponsorIdAndName()
            => Assert.Fail("pending: T449 GET /api/shows/{slug} sponsor — AC2");

        [Fact]
        public void AnUnlinkedShowCarriesSponsorNull()
            => Assert.Fail("pending: T449 GET /api/shows/{slug} sponsor null — AC2");
    }

    public sealed class ScenarioPatchAcceptsSponsorIdNullClears
    {
        [Fact]
        public void PatchWithNullIs200()
            => Assert.Fail("pending: T449 PATCH /api/shows/{slug} sponsorId null — AC3");

        [Fact]
        public void SponsorIsNullOnTheRow()
            => Assert.Fail("pending: T449 PATCH clears — AC3");
    }

    public sealed class ScenarioNoOnAirChange
    {
        [Fact]
        public void AShowWithASponsorRendersTheSameIdentTextAsOneWithout()
            => Assert.Fail("pending: T449 F175.2 ident unchanged — AC5");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioGuardsIncludeShows
    {
        [Fact]
        public void AnUnknownSponsorIdIs404SponsorNotFound()
            => Assert.Fail("pending: T449 PATCH 404 sponsor_not_found — AC4");

        [Fact]
        public void DeletingAShowsSponsorIs409SponsorInUseListingTheShow()
            => Assert.Fail("pending: T449 DELETE /api/sponsors 409 lists the show — AC6");

        [Fact]
        public void UninstallingThatSponsorsPackIs409AdPackInUseListingTheShow()
            => Assert.Fail("pending: T449 DELETE /api/ad-packs 409 lists the show — AC6");

        [Fact]
        public void NothingWasRemovedByEitherRefusal()
            => Assert.Fail("pending: T449 guards leave rows — AC6");
    }
}
