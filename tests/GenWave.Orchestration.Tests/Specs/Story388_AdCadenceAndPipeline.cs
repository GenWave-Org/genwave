// STORY-388 — An ad airs every N units, from whichever source answers first (F158.2/.3/.5 · pending T396/T397)

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

    public sealed class ScenarioThePipelineOrder
    {
        [Fact(Skip = "Pending T396 — see docs/PLAN.md")]
        public void FirstNonNullWinsInRegistrationOrder()
        {
            // Source A (null), source B (spot), floor C (spot): B's spot vends.
            Assert.Fail("pending T396");
        }

        [Fact(Skip = "Pending T396 — see docs/PLAN.md")]
        public void TheLibraryFloorAnswersWhenEveryPluginIsNull()
        {
            Assert.Fail("pending T396");
        }
    }

    public sealed class ScenarioAntiRepeat
    {
        [Fact(Skip = "Pending T396 — see docs/PLAN.md")]
        public void NoSpotRepeatsInsideTheWindow()
        {
            // AntiRepeatWindow=5, six ready spots, six vends: all distinct.
            Assert.Fail("pending T396");
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

        [Fact(Skip = "Pending T396 — see docs/PLAN.md")]
        public void AThrowingSourceIsWarnSkippedAndTheFloorStillAnswers()
        {
            Assert.Fail("pending T396");
        }
    }
}
