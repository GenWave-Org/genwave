// STORY-573 — Varied openers (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureVariedOpeners
{
    // AC1 — three of a DJ's lines in the last hour open "Ah, 2026"
    public sealed class ScenarioTheSameOpenerAgain
    {
        [Fact(Skip = "pending: T807")]
        public void TheNextLineIsFlagged() { }
    }

    // AC2 — the prompt
    public sealed class ScenarioThePromptIsShaped
    {
        [Fact(Skip = "pending: T807")]
        public void ItListsTheDjsRecentOpenersAsDontStartWithThese() { }
    }

    // AC3 — the shadow week's records, replayed
    public sealed class ScenarioTheShadowWeekReplayed
    {
        [Fact(Skip = "pending: T807")]
        public void UnderFivePercentOfDjLinesAreFlagged() { }

        [Fact(Skip = "pending: T807")]
        public void TheNovember22RunIsFlagged() { }
    }

    // Sad path
    // AC5 — another DJ used that opener
    public sealed class ScenarioAnotherDjsOpener
    {
        [Fact(Skip = "pending: T807")]
        public void NoFlag() { }
    }

    // Sad path
    // AC6 — the same opener only in lines over an hour old
    public sealed class ScenarioAnOldOpener
    {
        [Fact(Skip = "pending: T807")]
        public void NoFlag() { }
    }
}
