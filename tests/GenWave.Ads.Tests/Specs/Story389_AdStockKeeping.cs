// STORY-389 — The stock keeps itself (worker half: AC2–AC5 · F159.3/.4 · pending T402)

namespace GenWave.Ads.Tests.Specs;

public static class FeatureAdStockKeeping
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDraftByDefault
    {
        [Fact(Skip = "Pending T402 — see docs/PLAN.md")]
        public void AGeneratedSpotLandsInDraftWhenAutoApproveIsOff()
        {
            Assert.Fail("pending T402");
        }

        [Fact(Skip = "Pending T402 — see docs/PLAN.md")]
        public void TheWorkerNeverRendersADraft()
        {
            Assert.Fail("pending T402");
        }
    }

    public sealed class ScenarioAutoApproveFlowsThrough
    {
        [Fact(Skip = "Pending T402 — see docs/PLAN.md")]
        public void AGeneratedSpotLandsInApprovedWhenAutoApproveIsOn()
        {
            Assert.Fail("pending T402");
        }
    }

    public sealed class ScenarioRefreshRetiresAndRefills
    {
        [Fact(Skip = "Pending T402 — see docs/PLAN.md")]
        public void AStaleLlmSpotIsRetiredWithItsMediaRowIneligible()
        {
            // TargetCount=2, RefreshDays=30, a 31-day-old ready llm spot: retired,
            //   media eligible=false, never deleted.
            Assert.Fail("pending T402");
        }

        [Fact(Skip = "Pending T402 — see docs/PLAN.md")]
        public void GenerationRefillsTowardTargetCount()
        {
            Assert.Fail("pending T402");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheExemptAndTheStuck
    {
        [Fact(Skip = "Pending T402 — see docs/PLAN.md")]
        public void AnOwnerSpotIsNeverRefreshRetired()
        {
            Assert.Fail("pending T402");
        }

        [Fact(Skip = "Pending T402 — see docs/PLAN.md")]
        public void AStuckRenderingSpotReArmsToApprovedAfterTheGrace()
        {
            // The announcements guardian shape (F161 · STORY-391 AC6 shares the mechanism).
            Assert.Fail("pending T402");
        }
    }
}
