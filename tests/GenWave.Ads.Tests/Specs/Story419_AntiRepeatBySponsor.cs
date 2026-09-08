// STORY-419 — Anti-repeat is by sponsor, with a one-sponsor relaxation (SPEC F173.3 · PLAN T439)

namespace GenWave.Ads.Tests.Specs;

public static class FeatureAntiRepeatIsBySponsorWithAOneSponsorRelaxation
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheSameSponsorNeverAirsTwiceInARow
    {
        [Fact]
        public void TheSecondPickReturnsTheOtherSponsor()
            => Assert.Fail("pending: T439 window=1 — AC1");
    }

    public sealed class ScenarioTheWindowIsHonouredUpToN
    {
        [Fact]
        public void TheThirdPickReturnsTheThirdSponsor()
            => Assert.Fail("pending: T439 window=2 — AC2");
    }

    public sealed class ScenarioOneSponsorRelaxationLogsAndAirs
    {
        [Fact]
        public void ThePickReturnsTheOnlySponsorsSpot()
            => Assert.Fail("pending: T439 relaxation — AC3");

        [Fact]
        public void TheRelaxationLineIsLoggedExactlyOnceAtInformation()
            => Assert.Fail("pending: T439 \"Ad anti-repeat relaxed: one sponsor in rotation\" — AC3");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAPausedSponsorIsNeverRelaxedIntoAiring
    {
        [Fact]
        public void ThePickReturnsNull()
            => Assert.Fail("pending: T439 paused never relaxed — AC4");

        [Fact]
        public void NoRelaxationLineAppears()
            => Assert.Fail("pending: T439 paused never relaxed — AC4");
    }
}
