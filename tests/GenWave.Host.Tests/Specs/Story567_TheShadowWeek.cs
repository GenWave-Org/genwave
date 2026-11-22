// STORY-567 — The shadow week (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheShadowWeek
{
    // AC1 — a fresh station
    public sealed class ScenarioAFreshStation
    {
        [Fact(Skip = "pending: T788")]
        public void TheModeIsLegacy() { }

        [Fact(Skip = "pending: T788")]
        public void TheRuleBookIsNotCalled() { }
    }

    // AC2 — mode shadow, a reply the rules flag but today's code passes
    public sealed class ScenarioShadowAirsTodaysVerdict
    {
        [Fact(Skip = "pending: T788")]
        public void TheReplyAirs() { }

        [Fact(Skip = "pending: T788")]
        public void TheRowStoresTheRuleVerdicts() { }

        [Fact(Skip = "pending: T788")]
        public void TheRowStoresTheLegacyOutcome() { }
    }

    // AC3 — the mode changes
    public sealed class ScenarioTheModeChanges
    {
        [Fact(Skip = "pending: T788")]
        public void TheNextWrittenTalkUsesItWithNoRestart() { }
    }

    // AC4 — POST /api/talk-rules/replay {days: 7, compare: legacy}
    public sealed class ScenarioTheShadowReportIsRequested
    {
        [Fact(Skip = "pending: T791")]
        public void EachRuleListsTheLinesWhereTheVerdictsDiffer() { }

        [Fact(Skip = "pending: T791")]
        public void TheLinesATrimSavedAreCounted() { }
    }

    // AC5 — mode shadow, a written chat
    public sealed class ScenarioAChatIsWrittenInShadow
    {
        [Fact(Skip = "pending: T789")]
        public void AChatRowIsStored() { }
    }

    // AC5 — mode shadow, a written ad
    public sealed class ScenarioAnAdIsWrittenInShadow
    {
        [Fact(Skip = "pending: T790")]
        public void AnAdRowIsStored() { }

        [Fact(Skip = "pending: T790")]
        public void TheRulesSawTheTextBeforeThePhoneRewrite() { }
    }

    // Sad path
    // AC7 — PUT TalkRules:Mode nope
    public sealed class ScenarioABadMode
    {
        [Fact(Skip = "pending: T788")]
        public void ItIsRejectedWith400() { }
    }

    // Sad path
    // AC8 — the rule book throws in shadow
    public sealed class ScenarioTheRuleBookThrowsInShadow
    {
        [Fact(Skip = "pending: T788")]
        public void TheTalkAirsAsTodaysCodeDecided() { }

        [Fact(Skip = "pending: T788")]
        public void AWarningIsLogged() { }
    }
}
