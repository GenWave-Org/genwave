// STORY-570 — One flow for flagged talk (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureOneFlowForFlaggedTalk
{
    // AC1 — mode rules, a clean reply
    public sealed class ScenarioACleanReply
    {
        [Fact(Skip = "pending: T800")]
        public void TheAiredTextEqualsTheCleanedText() { }
    }

    // AC2 — a reply two rules flag
    public sealed class ScenarioAReplyTwoRulesFlag
    {
        [Fact(Skip = "pending: T800")]
        public void ExactlyOneMoreLlmCallIsMade() { }

        [Fact(Skip = "pending: T800")]
        public void TheReAskListsBothAsks() { }

        [Fact(Skip = "pending: T800")]
        public void TheAsksAreSortedByRuleId() { }
    }

    // AC3 — a DJ line still flagged after the re-ask
    public sealed class ScenarioADjLineIsStillFlagged
    {
        [Fact(Skip = "pending: T800")]
        public void TemplateCopyAirs() { }

        [Fact(Skip = "pending: T800")]
        public void TheTalkIsDroppedWhereTemplateCopyNeverAirs() { }
    }

    // AC4 — a chat still flagged after the re-ask
    public sealed class ScenarioAChatIsStillFlagged
    {
        [Fact(Skip = "pending: T801")]
        public void TheChatIsDropped() { }

        [Fact(Skip = "pending: T801")]
        public void OnlyOneReAskWasMade() { }
    }

    // AC7 — the golden prompts after the shapers moved
    public sealed class ScenarioTheGoldenPromptsAfterTheMove
    {
        [Fact(Skip = "pending: T800")]
        public void TheOnlyDifferenceIsTextMovedUnderForThisBreak() { }
    }

    // AC8 — a flag that changes the outcome
    public sealed class ScenarioAFlagChangesTheOutcome
    {
        [Fact(Skip = "pending: T800")]
        public void ARuleFlaggedRowNamesTheRule() { }

        [Fact(Skip = "pending: T800")]
        public void TheRowNamesTheDj() { }

        [Fact(Skip = "pending: T800")]
        public void TheRowNamesTheOutcome() { }
    }

    // AC9 — a callback offered and the talk rejected
    public sealed class ScenarioACallbackOnRejectedTalk
    {
        [Fact(Skip = "pending: T800")]
        public void TheCallbackIsNotSettled() { }
    }

    // Sad path
    // AC10 — an empty reply
    public sealed class ScenarioAnEmptyReply
    {
        [Fact(Skip = "pending: T800")]
        public void NoRuleRuns() { }

        [Fact(Skip = "pending: T800")]
        public void NoReAskIsMade() { }
    }

    // Sad path
    // AC12 — a reply with one repeated sentence
    public sealed class ScenarioARepeatedSentence
    {
        [Fact(Skip = "pending: T800")]
        public void TheSentenceIsNotCut() { }

        [Fact(Skip = "pending: T800")]
        public void TheReplyIsReAsked() { }
    }
}
