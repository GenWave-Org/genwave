// STORY-411 — A brief belongs to exactly one sponsor (SPEC F171.6 · PLAN T435)

namespace GenWave.Host.Tests.Specs;

public static class FeatureABriefBelongsToExactlyOneSponsor
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheBriefCarriesTheSponsorObject
    {
        [Fact]
        public void PostWithASponsorIs201()
            => Assert.Fail("pending: T435 POST /api/ad-briefs sponsorId — AC2");

        [Fact]
        public void TheResponseCarriesSponsorIdNamePaused()
            => Assert.Fail("pending: T435 response sponsor object — AC2");

        [Fact]
        public void TheResponseHasNoBrandField()
            => Assert.Fail("pending: T435 no brand property — AC2");
    }

    public sealed class ScenarioADifferentAngleUnderTheSameSponsorIsAllowed
    {
        [Fact]
        public void ASecondAngleIs201()
            => Assert.Fail("pending: T435 several angles per sponsor — AC4");
    }

    public sealed class ScenarioTheBrandColumnIsGone
    {
        [Fact]
        public void AdBriefHasNoBrandColumn()
            => Assert.Fail("pending: T435 db/46 drops ad_brief.brand — AC5");

        [Fact]
        public void SponsorIdIsNotNull()
            => Assert.Fail("pending: T435 db/46 ad_brief.sponsor_id NOT NULL — AC5");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingInvalidInput
    {
        [Fact]
        public void MissingSponsorIdIs400SponsorRequired()
            => Assert.Fail("pending: T435 POST sponsor_required — AC1");

        [Fact]
        public void TheSameAngleTwiceIs409DuplicateBrief()
            => Assert.Fail("pending: T435 POST duplicate_brief on folded premise — AC3");
    }
}
