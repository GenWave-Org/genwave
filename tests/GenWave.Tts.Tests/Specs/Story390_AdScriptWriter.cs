// STORY-390 — The station writes its own ads (writer half: AC2/AC3 · F160.1/.2 · pending T400)

namespace GenWave.Tts.Tests.Specs;

public static class FeatureAdScriptWriter
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioOneCompletionOneRecordedCall
    {
        [Fact(Skip = "Pending T400 — see docs/PLAN.md")]
        public void AGenerationRecordsOneAdScriptCallInTheRing()
        {
            // LlmCallRing gains exactly one LlmCallKind.AdScript entry per attempt (F160.1).
            Assert.Fail("pending T400");
        }

        [Fact(Skip = "Pending T400 — see docs/PLAN.md")]
        public void ThePromptCarriesTheSpotStructureAndTheBrief()
        {
            // Structure-first (F160.2): the 30s template beats + the sampled brief's
            //   brand/premise/tone all appear in the prompt.
            Assert.Fail("pending T400");
        }
    }

    public sealed class ScenarioTheReAskLadder
    {
        [Fact(Skip = "Pending T400 — see docs/PLAN.md")]
        public void OneViolationTriggersExactlyOneReAskNamingTheRule()
        {
            Assert.Fail("pending T400");
        }

        [Fact(Skip = "Pending T400 — see docs/PLAN.md")]
        public void ACleanSecondDraftPasses()
        {
            Assert.Fail("pending T400");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — skip-only, no floor
    // ---------------------------------------------------------------------

    public sealed class ScenarioSkipOnlyNoTemplateFloor
    {
        [Fact(Skip = "Pending T400 — see docs/PLAN.md")]
        public void AFailedLlmProducesNoSpotAndNoCannedAd()
        {
            // Timeout/refusal: nothing advances — a canned parody ad is worse than none (F160.1).
            Assert.Fail("pending T400");
        }

        [Fact(Skip = "Pending T400 — see docs/PLAN.md")]
        public void ASecondViolationFailsTheSpotWithTheRuleId()
        {
            // fail_reason = the rule id (the F138 ladder shape).
            Assert.Fail("pending T400");
        }
    }
}
