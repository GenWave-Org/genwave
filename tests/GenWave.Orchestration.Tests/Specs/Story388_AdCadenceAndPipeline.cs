// STORY-388 — An ad airs every N units, from whichever source answers first (F158.2/.3/.5 · pending T397)
//
// PLAN T396 moved this file's own AC3/AC4/AC6 facts (ScenarioThePipelineOrder, ScenarioAntiRepeat,
// AThrowingSourceIsWarnSkippedAndTheFloorStillAnswers) to GenWave.Ads.Tests: AdSpotPipeline and
// LibraryAdSpotSource — the classes those facts exercise — live in GenWave.Ads, which
// GenWave.Orchestration.Tests does not (and should not) reference. See
// GenWave.Ads.Tests/Specs/Story388_AdSpotPipeline.cs and
// GenWave.Ads.Tests/Specs/Story388_LibraryAdSpotSource.cs — the story tag traveled with them, green
// there. What remains here is PLAN T397's own: the Orchestrator cadence wiring (the deferral, the
// drain, the KickResolved vend) this pipeline plugs into.

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureAdCadenceAndPipeline
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheCadenceTriggersAVend
    {
        [Fact(Skip = "Pending T397 — see docs/PLAN.md")]
        public void TheSecondUnitEnqueuesAnAdDeferral()
        {
            // EveryNUnits=2, a fake IAdSpotSource returning a resolved item:
            //   unit 2 enqueues SpeechDeferralKind.Ad (unit 0 never does — the StationId twin).
            Assert.Fail("pending T397");
        }

        [Fact(Skip = "Pending T397 — see docs/PLAN.md")]
        public void TheAdDrainsAfterTheStationIdArmAndBeforeTheLeadIn()
        {
            // Assembled unit order: back-announce … station-id, AD, lead-in (F158.3).
            Assert.Fail("pending T397");
        }

        [Fact(Skip = "Pending T397 — see docs/PLAN.md")]
        public void TheVendIsResolvedNeverRenderedAtAir()
        {
            // The vended item enters via KickResolved — zero synthesizer calls at assembly.
            Assert.Fail("pending T397");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioQuietAndFailingSources
    {
        [Fact(Skip = "Pending T397 — see docs/PLAN.md")]
        public void ZeroDisablesTheTriggerEntirely()
        {
            Assert.Fail("pending T397");
        }

        [Fact(Skip = "Pending T397 — see docs/PLAN.md")]
        public void AnEmptyPipelineAssemblesTheBreakWithOneInfoNeverAWarn()
        {
            // Null answer = a normal day one (F158.3).
            Assert.Fail("pending T397");
        }
    }
}
