// STORY-407 — List sponsors and read one (SPEC F171.2 · PLAN T434)

namespace GenWave.Host.Tests.Specs;

public static class FeatureListSponsorsAndReadOne
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheListShape
    {
        [Fact]
        public void TheListIsOrderedByName()
            => Assert.Fail("pending: T434 GET /api/sponsors ordering — AC1");

        [Fact]
        public void EveryRowCarriesTheTenFactsAndPausedAndPackSlug()
            => Assert.Fail("pending: T434 GET /api/sponsors row shape — AC1");

        [Fact]
        public void EveryRowCarriesBriefsSpotsByStateAndShowsCounts()
            => Assert.Fail("pending: T434 GET /api/sponsors counts — AC1");
    }

    public sealed class ScenarioTheFoldedFilter
    {
        [Fact]
        public void QMatchesLikeNameKey()
            => Assert.Fail("pending: T434 GET /api/sponsors?q= folds case + spacing — AC2");
    }

    public sealed class ScenarioTheDetailCarriesAWeakETag
    {
        [Fact]
        public void GetByIdIs200()
            => Assert.Fail("pending: T434 GET /api/sponsors/{id} — AC3");

        [Fact]
        public void TheETagIsWeakDigits()
            => Assert.Fail("pending: T434 ETag W/\"<xmin>\" — AC3");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnonymousIsRefused
    {
        [Fact]
        public void NoCookieIs401()
            => Assert.Fail("pending: T434 AdminSurface + Operator policy — AC4");
    }
}
