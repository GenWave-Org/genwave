// STORY-570 — One flow for flagged talk (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureRuleBookShapesAndRecords
{
    // AC6 — two switched-on shapers
    public sealed class ScenarioShapersAddNotes
    {
        [Fact(Skip = "pending: T795")]
        public void EachNoteIsUnderForThisBreak() { }

        [Fact(Skip = "pending: T795")]
        public void TheNotesAreSortedByRuleId() { }

        [Fact(Skip = "pending: T795")]
        public void EachMemoIsOnTheSnapshot() { }
    }

    // AC9 — accepted talk only
    public sealed class ScenarioRecordingRules
    {
        [Fact(Skip = "pending: T795")]
        public void AcceptedTalkReachesTheRecordingRule() { }

        [Fact(Skip = "pending: T795")]
        public void RejectedTalkDoesNot() { }
    }

    // Sad path
    // AC11 — a switched-off shaper
    public sealed class ScenarioAShaperIsSwitchedOff
    {
        [Fact(Skip = "pending: T795")]
        public void ThePromptHasNoNoteFromIt() { }

        [Fact(Skip = "pending: T795")]
        public void ItIsNotCalled() { }
    }
}
