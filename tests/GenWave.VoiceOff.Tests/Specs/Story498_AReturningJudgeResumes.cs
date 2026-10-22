// STORY-498 — a returning judge resumes (gh-#614 · SPEC brief extra · PLAN T617)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureAReturningJudgeResumes
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — voted pairs are skipped
    public sealed class ScenarioVotedPairsAreSkipped
    {
        // Given "Dean" voted 12 pairs of a session
        // When  Dean reloads the page

        [Fact(Skip = "pending: T617 — see docs/PLAN.md")]
        public void TheFirstPairShownIsOneDeanHasNotVoted() => Assert.Fail("pending: T617 — the first pair shown is one Dean has not voted");

        [Fact(Skip = "pending: T617 — see docs/PLAN.md")]
        public void ProgressReads12Of40() => Assert.Fail("pending: T617 — progress reads 12 of 40");
    }

    // AC2 — the other judge is unaffected
    public sealed class ScenarioTheOtherJudgeIsUnaffected
    {
        // Given "Dean" voted 12 pairs
        // When  a second judge loads the page

        [Fact(Skip = "pending: T617 — see docs/PLAN.md")]
        public void ProgressReads0Of40() => Assert.Fail("pending: T617 — progress reads 0 of 40");
    }
}
