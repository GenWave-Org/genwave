// STORY-526 — serve keeps the judgement (gh-#614 · SPEC F217 · PLAN T664)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-26); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureServeKeepsTheJudgement
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — state lists every card
    public sealed class ScenarioStateListsEveryCard
    {
        // Given a run with two rendered cards and one failed
        // When  GET /state

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void ThreeCardsAreReturned() => Assert.Fail("pending: T664 — three cards are returned");

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void EachRenderedCardCarriesItsFacts() => Assert.Fail("pending: T664 — each rendered card carries its facts");

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void TheFailedCardCarriesItsReason() => Assert.Fail("pending: T664 — the failed card carries its reason");
    }

    // AC2 — clips are served
    public sealed class ScenarioClipsAreServed
    {
        // Given the run
        // When  GET /clips/<card>.wav

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void TheMatchedClipBytesComeBack() => Assert.Fail("pending: T664 — the matched clip bytes come back");

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void TheContentTypeIsAudioWav() => Assert.Fail("pending: T664 — the content type is audio/wav");
    }

    // AC3 — a judgement is saved
    public sealed class ScenarioAJudgementIsSaved
    {
        // Given a judgement with order, winces, stars and notes
        // When  PUT /judgement

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void JudgementJsonHoldsTheOrder() => Assert.Fail("pending: T664 — judgement.json holds the order");

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void JudgementJsonHoldsTheWincesStarsAndNotes() => Assert.Fail("pending: T664 — judgement.json holds the winces, stars and notes");

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void SavedAtIsSet() => Assert.Fail("pending: T664 — savedAt is set");
    }

    // AC4 — state returns the judgement
    public sealed class ScenarioStateReturnsTheJudgement
    {
        // Given the saved judgement
        // When  GET /state

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void TheOrderIsReturnedAsSaved() => Assert.Fail("pending: T664 — the order is returned as saved");

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void TheNotesAreReturnedAsSaved() => Assert.Fail("pending: T664 — the notes are returned as saved");
    }

    // AC5 — missing cards sort last
    public sealed class ScenarioMissingCardsSortLast
    {
        // Given an order naming one of three cards
        // When  GET /state

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void TheUnnamedCardsFollowInIdOrder() => Assert.Fail("pending: T664 — the unnamed cards follow in id order");
    }

    // AC6 — the LAN URL is printed
    public sealed class ScenarioTheLANURLIsPrinted
    {
        // Given serve starts
        // When  stdout is read

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void ItPrintsAURLOnALANAddress() => Assert.Fail("pending: T664 — it prints a URL on a LAN address");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — an unknown card id is refused
    public sealed class ScenarioAnUnknownCardIdIsRefused
    {
        // Given an order naming a card not in the run
        // When  PUT /judgement

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void TheReplyIs400() => Assert.Fail("pending: T664 — the reply is 400");

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void JudgementJsonIsUnchanged() => Assert.Fail("pending: T664 — judgement.json is unchanged");
    }

    // AC8 — an oversized body is refused
    public sealed class ScenarioAnOversizedBodyIsRefused
    {
        // Given a body over 64 KB
        // When  PUT /judgement

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void TheReplyIs413() => Assert.Fail("pending: T664 — the reply is 413");
    }

    // AC9 — out-of-range scores are refused
    public sealed class ScenarioOutOfRangeScoresAreRefused
    {
        // Given stars 6 and winces −1
        // When  PUT /judgement

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void TheReplyIs400() => Assert.Fail("pending: T664 — the reply is 400");
    }

    // AC10 — traversal is refused
    public sealed class ScenarioTraversalIsRefused
    {
        // Given GET /clips/../run.json
        // When  the request is made

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void TheReplyIs404() => Assert.Fail("pending: T664 — the reply is 404");
    }

    // AC11 — malformed JSON is refused
    public sealed class ScenarioMalformedJSONIsRefused
    {
        // Given a body that is not JSON
        // When  PUT /judgement

        [Fact(Skip = "pending: T664 — see docs/PLAN.md")]
        public void TheReplyIs400() => Assert.Fail("pending: T664 — the reply is 400");
    }
}
