// STORY-420 — Stock refill counts only unpaused sponsors (SPEC F173.4/F173.5 · PLAN T440)

namespace GenWave.Ads.Tests.Specs;

public static class FeatureStockRefillCountsOnlyUnpausedSponsors
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioPausedSponsorsStockIsNotCounted
    {
        [Fact]
        public void TheWorkerGeneratesFromOtherSponsorsBriefs()
            => Assert.Fail("pending: T440 CountStockGeneratedAsync excludes paused — AC1");

        [Fact]
        public void TheBelowTargetLineIsLogged()
            => Assert.Fail("pending: T440 \"Ad stock below target: generating one\" — AC1");
    }

    public sealed class ScenarioRepairAndGuardianAreUnchanged
    {
        [Fact]
        public void RepairReEnablesAPausedSponsorsDisabledMediaRow()
            => Assert.Fail("pending: T440 F173.5 repair does not read paused — AC2");
    }
}
