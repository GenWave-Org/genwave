// STORY-408 — Create and edit a sponsor (SPEC F171.3 · PLAN T434)

namespace GenWave.Host.Tests.Specs;

public static class FeatureCreateAndEditASponsor
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioCreateWithJustAName
    {
        [Fact]
        public void PostIs201()
            => Assert.Fail("pending: T434 POST /api/sponsors — AC1");

        [Fact]
        public void EveryFactIsNullExceptName()
            => Assert.Fail("pending: T434 POST /api/sponsors defaults — AC1");
    }

    public sealed class ScenarioEditAnyFactIncludingTheName
    {
        [Fact]
        public void PatchWithIfMatchIs200()
            => Assert.Fail("pending: T434 PATCH /api/sponsors/{id} — AC2");

        [Fact]
        public void TheTwoFieldsAreSet()
            => Assert.Fail("pending: T434 PATCH tagline + phone — AC2");
    }

    public sealed class ScenarioAPackOwnedSponsorsFactsStayEditable
    {
        [Fact]
        public void PatchingTaglineOnAPackOwnedSponsorIs200()
            => Assert.Fail("pending: T434 PATCH pack-owned facts — AC4");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingInvalidInput
    {
        [Fact]
        public void PackSlugInARequestIs400SponsorPackSlugForbidden()
            => Assert.Fail("pending: T434 POST packSlug refused — AC3");

        [Fact]
        public void RenamingAPackOwnedSponsorIs400SponsorNamePackOwned()
            => Assert.Fail("pending: T434 PATCH name read-only — AC4");

        [Fact]
        public void ANonUrlWebsiteIs400NamingTheField()
            => Assert.Fail("pending: T434 POST website shape — AC5");
    }
}
