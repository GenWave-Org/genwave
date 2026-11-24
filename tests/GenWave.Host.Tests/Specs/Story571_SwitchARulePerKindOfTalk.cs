// STORY-571 — Switch a rule per kind of talk (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

public static class FeatureSwitchARulePerKindOfTalk
{
    // AC1 — GET /api/talk-rules through the running host
    public sealed class ScenarioTheRulesAreListed
    {
        [Fact(Skip = "pending: T803")]
        public void EachRuleHasItsId() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasItsName() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasWhatItDoes() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasWhy() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasAnExample() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasTheTalkItCanCover() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasASwitchPerKind() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasLastWeeksFlagsPerKind() { }
    }

    // AC2 — no switch row
    public sealed class ScenarioNoSwitchRow
    {
        [Fact(Skip = "pending: T799")]
        public void TheSwitchShowsTheRulesDefault() { }
    }

    // AC3, AC4 — PUT /api/talk-rules/repeat-phrases/chat {enabled: false}
    public sealed class ScenarioARuleIsSwitchedOffForChats
    {
        [Fact(Skip = "pending: T803")]
        public void TheNextChatAttemptRecordsTheRuleAsOff() { }

        [Fact(Skip = "pending: T803")]
        public void DjLinesAreStillJudgedByIt() { }

        [Fact(Skip = "pending: T803")]
        public void NoRestartIsNeeded() { }
    }

    // AC5 — GET /api/talk-rules/{id}/flags?days=7
    public sealed class ScenarioFlaggedLinesAreRequested
    {
        [Fact(Skip = "pending: T803")]
        public void TheLatestTwentyComeBack() { }

        [Fact(Skip = "pending: T803")]
        public void EachHasWhenDjKindWordsAndOutcome() { }
    }

    // AC6 — a rule declared in the test assembly
    public sealed class ScenarioAOneFileRule
    {
        [Fact(Skip = "pending: T803")]
        public void TheApiListsIt() { }
    }

    // Sad path
    // AC7 — PUT /api/talk-rules/phone-number/dj-line {enabled: true}
    public sealed class ScenarioAKindTheRuleCannotCover
    {
        [Fact(Skip = "pending: T803")]
        public void ItIsRejectedWith400() { }
    }

    // Sad path
    // AC8 — PUT /api/talk-rules/nope/chat
    public sealed class ScenarioAnUnknownRule
    {
        [Fact(Skip = "pending: T803")]
        public void ItReturns404() { }
    }

    // Sad path
    // AC9 — no admin cookie
    public sealed class ScenarioNoAdminCookie
    {
        [Fact(Skip = "pending: T803")]
        public void TheListReturns401() { }

        [Fact(Skip = "pending: T803")]
        public void TheSwitchReturns401() { }

        [Fact(Skip = "pending: T803")]
        public void TheFlagsReturn401() { }
    }
}
