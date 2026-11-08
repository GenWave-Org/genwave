// STORY-535 — Crosstalk does not repeat either (gh-#293 gh-#920 · SPEC F218 · PLAN T694)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureCrosstalkDoesNotRepeatEither
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — each speaker, their own history
    // entry point: CrosstalkScriptWriter (handoff exchange written by the Orchestrator)
    public sealed class ScenarioEachSpeakerTheirOwnHistory
    {
        // Given personas A and B with their own utterances
        // When  an exchange is written

        [Fact(Skip = "pending: T694 — see docs/PLAN.md")]
        public void ASLinesAreCheckedOnlyAgainstASHistoryAndBSAgainstBS() => Assert.Fail("pending: T694 — A's lines are checked only against A's history and B's against B's");
    }

    // AC2 — each line is on the record
    public sealed class ScenarioEachLineIsOnTheRecord
    {
        // Given an accepted exchange of four lines
        // When  it is stored

        [Fact(Skip = "pending: T694 — see docs/PLAN.md")]
        public void FourUtteranceRowsExistKindCrosstalkSegmentKindCrosstalkLineTwoPerPersona() => Assert.Fail("pending: T694 — four utterance rows exist, kind crosstalk, segment_kind CrosstalkLine, two per persona");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC3 — a stubborn line is dropped, not replaced
    public sealed class ScenarioAStubbornLineIsDroppedNotReplaced
    {
        // Given speaker A's line repeats after one re-ask
        // When  the exchange is assembled

        [Fact(Skip = "pending: T694 — see docs/PLAN.md")]
        public void ASLineIsAbsentAndBSLinesAreIntact() => Assert.Fail("pending: T694 — A's line is absent and B's lines are intact");

        [Fact(Skip = "pending: T694 — see docs/PLAN.md")]
        public void NoTemplateTextIsInserted() => Assert.Fail("pending: T694 — no template text is inserted");
    }
}
