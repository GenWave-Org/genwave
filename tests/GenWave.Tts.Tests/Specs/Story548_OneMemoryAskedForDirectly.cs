// STORY-548 — One memory, asked for directly (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. AC1–AC3 are prompt-content facts on LlmPromptBuilder (T735); AC4–AC7 drive LlmCopyWriter with a fake planner (T736).
// Every fact is pending at plan time (2026-11-16); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureOneMemoryAskedForDirectly
{
    // AC1 — the ask
    public sealed class ScenarioTheAsk
    {
        // Given a callback ask for "has a dog named Daisy" on a track intro
        // When  the on-air prompt is built

        [Fact(Skip = "pending: T735 — see docs/PLAN.md")]
        public void ItContainsSomethingYouRememberHasADogNamedDaisy() => Assert.Fail("pending: T735 — it contains something you remember has a dog named Daisy");

        [Fact(Skip = "pending: T735 — see docs/PLAN.md")]
        public void TheNextLineAsksForItInAFewWordsAndForbidsSayingItIsRemembered() => Assert.Fail("pending: T735 — the next line asks for it in a few words and forbids saying it is remembered");

        [Fact(Skip = "pending: T735 — see docs/PLAN.md")]
        public void TheAskSitsDirectlyBeforeTheSegmentInstruction() => Assert.Fail("pending: T735 — the ask sits directly before the segment instruction");
    }

    // AC2 — no ask, no memory rows
    public sealed class ScenarioNoAskNoMemoryRows
    {
        // Given no callback ask
        // When  the on-air prompt is built

        [Fact(Skip = "pending: T735 — see docs/PLAN.md")]
        public void ItContainsNoSomethingYouRememberLine() => Assert.Fail("pending: T735 — it contains no something you remember line");

        [Fact(Skip = "pending: T735 — see docs/PLAN.md")]
        public void ItContainsNoWhatYouRememberBlock() => Assert.Fail("pending: T735 — it contains no what you remember block");
    }

    // AC3 — the never-mention list stays
    public sealed class ScenarioTheNeverMentionListStays
    {
        // Given a claim forgotten 2 days ago and no callback ask

        [Fact(Skip = "pending: T735 — see docs/PLAN.md")]
        public void TheNeverMentionListStillNamesTheClaim() => Assert.Fail("pending: T735 — the never mention list still names the claim");
    }

    // AC4 — only intros and back-announces
    public sealed class ScenarioOnlyIntrosAndBackAnnounces
    {
        // Given an eligible memory and a draw that always passes
        // When  a sign-on, sign-off, context segment and crosstalk exchange are written

        [Fact(Skip = "pending: T736 — see docs/PLAN.md")]
        public void ThePlannerIsNeverAskedForThoseSegmentKinds() => Assert.Fail("pending: T736 — the planner is never asked for those segment kinds");

        [Fact(Skip = "pending: T736 — see docs/PLAN.md")]
        public void NoPromptCarriesAnAsk() => Assert.Fail("pending: T736 — no prompt carries an ask");
    }

    // AC5 — on air, the writer asks
    public sealed class ScenarioOnAirTheWriterAsks
    {
        // When  a track intro is written on air

        [Fact(Skip = "pending: T736 — see docs/PLAN.md")]
        public void ThePromptSentToTheModelCarriesTheAskForThatMemory() => Assert.Fail("pending: T736 — the prompt sent to the model carries the ask for that memory");
    }

    // AC6 — preview never asks (sad path)
    public sealed class ScenarioPreviewNeverAsks
    {
        // When  a preview is written

        [Fact(Skip = "pending: T736 — see docs/PLAN.md")]
        public void NoOfferIsWritten() => Assert.Fail("pending: T736 — no offer is written");

        [Fact(Skip = "pending: T736 — see docs/PLAN.md")]
        public void ThePreviewPromptCarriesNoAsk() => Assert.Fail("pending: T736 — the preview prompt carries no ask");
    }

    // AC7 — memory off (sad path)
    public sealed class ScenarioMemoryOff
    {
        // Given the model's context is under 8192 tokens

        [Fact(Skip = "pending: T736 — see docs/PLAN.md")]
        public void NoOfferIsWrittenWhenMemoryIsOff() => Assert.Fail("pending: T736 — no offer is written when memory is off");
    }
}
