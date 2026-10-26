// STORY-527 — The card page (gh-#614 · SPEC F217 · PLAN T665)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-26); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheCardPage
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the page is served
    public sealed class ScenarioThePageIsServed
    {
        // Given a run
        // When  GET /

        [Fact(Skip = "pending: T665 — see docs/PLAN.md")]
        public void TheReplyIs200TextHtml() => Assert.Fail("pending: T665 — the reply is 200 text/html");

        [Fact(Skip = "pending: T665 — see docs/PLAN.md")]
        public void ThePageContainsACardTemplate() => Assert.Fail("pending: T665 — the page contains a card template");

        [Fact(Skip = "pending: T665 — see docs/PLAN.md")]
        public void ThePageContainsAPlayerWithAProgressBar() => Assert.Fail("pending: T665 — the page contains a player with a progress bar");

        [Fact(Skip = "pending: T665 — see docs/PLAN.md")]
        public void ThePageContainsADragHandle() => Assert.Fail("pending: T665 — the page contains a drag handle");

        [Fact(Skip = "pending: T665 — see docs/PLAN.md")]
        public void ThePageContainsAWinceCounter() => Assert.Fail("pending: T665 — the page contains a wince counter");

        [Fact(Skip = "pending: T665 — see docs/PLAN.md")]
        public void ThePageContainsFiveStars() => Assert.Fail("pending: T665 — the page contains five stars");

        [Fact(Skip = "pending: T665 — see docs/PLAN.md")]
        public void ThePageContainsANotesField() => Assert.Fail("pending: T665 — the page contains a notes field");

        [Fact(Skip = "pending: T665 — see docs/PLAN.md")]
        public void ThePageContainsADidNotRenderTray() => Assert.Fail("pending: T665 — the page contains a did-not-render tray");
    }

    // AC2 — the page is told nothing it should not know
    public sealed class ScenarioThePageIsToldNothingItShouldNotKnow
    {
        // Given the served HTML
        // When  it is searched

        [Fact(Skip = "pending: T665 — see docs/PLAN.md")]
        public void NoEngineSlugFromEnginesAppears() => Assert.Fail("pending: T665 — no engine slug from engines/ appears");

        [Fact(Skip = "pending: T665 — see docs/PLAN.md")]
        public void NoJudgeNameFieldAppears() => Assert.Fail("pending: T665 — no judge name field appears");
    }

    // AC3 — the page saves whole
    public sealed class ScenarioThePageSavesWhole
    {
        // Given the page script
        // When  it is read

        [Fact(Skip = "pending: T665 — see docs/PLAN.md")]
        public void ItPUTsJudgementWithOrderCardsAndSavedAt() => Assert.Fail("pending: T665 — it PUTs /judgement with order, cards and savedAt");
    }

    // AC4 — the page restores on open
    public sealed class ScenarioThePageRestoresOnOpen
    {
        // Given the page script
        // When  it is read

        [Fact(Skip = "pending: T665 — see docs/PLAN.md")]
        public void ItGETsStateBeforeRenderingCards() => Assert.Fail("pending: T665 — it GETs /state before rendering cards");
    }
}
