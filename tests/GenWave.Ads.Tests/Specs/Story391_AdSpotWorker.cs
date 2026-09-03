// STORY-391 — Spots render OFF the clock (worker half: AC4 · F161.1 · pending T402)
// AC6 (the stuck-rendering guardian) is specced with the stock pass in Story389_AdStockKeeping.cs.

namespace GenWave.Ads.Tests.Specs;

public static class FeatureAdSpotWorker
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioOffTheAirClock
    {
        [Fact(Skip = "Pending T402 — see docs/PLAN.md")]
        public void NoRenderStartsWhileABreakWindowIsOpen()
        {
            // The OnAirRenderGate read (the CrosstalkStockWorker posture, F161.1).
            Assert.Fail("pending T402");
        }

        [Fact(Skip = "Pending T402 — see docs/PLAN.md")]
        public void AnInFlightRenderIsCanceledWhenTheWindowOpens()
        {
            Assert.Fail("pending T402");
        }

        [Fact(Skip = "Pending T402 — see docs/PLAN.md")]
        public void RenderingResumesAfterTheWindowCloses()
        {
            Assert.Fail("pending T402");
        }
    }

    public sealed class ScenarioOneSpotPerTick
    {
        [Fact(Skip = "Pending T402 — see docs/PLAN.md")]
        public void TwoApprovedSpotsTakeTwoTicks()
        {
            Assert.Fail("pending T402");
        }
    }
}
