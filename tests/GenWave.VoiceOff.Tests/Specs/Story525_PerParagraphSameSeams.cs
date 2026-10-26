// STORY-525 — Per paragraph, same seams (gh-#614 · SPEC F217 · PLAN T663)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-26); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeaturePerParagraphSameSeams
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — eight requests per card
    public sealed class ScenarioEightRequestsPerCard
    {
        // Given one card and a stub engine
        // When  render runs

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void TheStubReceivedEightSpeechRequests() => Assert.Fail("pending: T663 — the stub received eight speech requests");

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void TheRequestsArriveInParagraphOrder() => Assert.Fail("pending: T663 — the requests arrive in paragraph order");
    }

    // AC2 — the request carries the card
    public sealed class ScenarioTheRequestCarriesTheCard
    {
        // Given the same run
        // When  the request bodies are read

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void EachBodyCarriesTheCardSVoice() => Assert.Fail("pending: T663 — each body carries the card's voice");

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void EachBodyCarriesTheCardSSpeed() => Assert.Fail("pending: T663 — each body carries the card's speed");

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void EachBodyCarriesTheCardSSettingsObject() => Assert.Fail("pending: T663 — each body carries the card's settings object");
    }

    // AC3 — raw paragraphs are kept
    public sealed class ScenarioRawParagraphsAreKept
    {
        // Given the same run
        // When  raw/<card>/ is listed

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void EightWavFilesNumbered1To8Exist() => Assert.Fail("pending: T663 — eight wav files numbered 1 to 8 exist");
    }

    // AC4 — the paragraphs are joined
    public sealed class ScenarioTheParagraphsAreJoined
    {
        // Given the same run
        // When  cards/<card>.wav is read

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void ItExists() => Assert.Fail("pending: T663 — it exists");

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void ItsDurationIsAtLeastTheSumOfTheEightPartsPlusSevenGaps() => Assert.Fail("pending: T663 — its duration is at least the sum of the eight parts plus seven gaps");
    }

    // AC5 — the clip is loudness-matched
    public sealed class ScenarioTheClipIsLoudnessMatched
    {
        // Given the same run
        // When  the clip's loudness is measured

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void IntegratedLoudnessIsWithin1LUOfMinus16() => Assert.Fail("pending: T663 — integrated loudness is within 1 LU of −16");
    }

    // AC6 — renders.jsonl records every paragraph
    public sealed class ScenarioRendersJsonlRecordsEveryParagraph
    {
        // Given the same run
        // When  renders.jsonl is read

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void ItHasEightLinesForTheCard() => Assert.Fail("pending: T663 — it has eight lines for the card");

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void EachLineCarriesCardNCharsWallMsAudioMsAndOk() => Assert.Fail("pending: T663 — each line carries card, n, chars, wallMs, audioMs and ok");
    }

    // AC7 — run.json records the run
    public sealed class ScenarioRunJsonRecordsTheRun
    {
        // Given the same run
        // When  run.json is read

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void ItCarriesTheScriptHash() => Assert.Fail("pending: T663 — it carries the script hash");

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void ItListsEachCardWithItsHost() => Assert.Fail("pending: T663 — it lists each card with its host");

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void ItMarksTheCardRendered() => Assert.Fail("pending: T663 — it marks the card rendered");
    }

    // AC8 — one card re-renders in place
    public sealed class ScenarioOneCardReRendersInPlace
    {
        // Given a finished run and render --card for one card
        // When  render runs

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void ThatCardSClipIsRewritten() => Assert.Fail("pending: T663 — that card's clip is rewritten");

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void EveryOtherCardSClipIsByteIdenticalToBefore() => Assert.Fail("pending: T663 — every other card's clip is byte-identical to before");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC9 — a failed container gate is a card
    public sealed class ScenarioAFailedContainerGateIsACard
    {
        // Given a card whose container is not running
        // When  render runs

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void TheCardIsRecordedRenderedFalseWithReasonContainer() => Assert.Fail("pending: T663 — the card is recorded rendered:false with reason container");

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void TheRunExits0() => Assert.Fail("pending: T663 — the run exits 0");
    }

    // AC10 — a failed paragraph is a card
    public sealed class ScenarioAFailedParagraphIsACard
    {
        // Given a stub returning 500 on paragraph 5
        // When  render runs

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void TheCardIsRecordedRenderedFalseNamingParagraph5() => Assert.Fail("pending: T663 — the card is recorded rendered:false naming paragraph 5");

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void TheNextCardStillRenders() => Assert.Fail("pending: T663 — the next card still renders");
    }

    // AC11 — a words-law failure sends nothing
    public sealed class ScenarioAWordsLawFailureSendsNothing
    {
        // Given a card whose override changes a word
        // When  render runs

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void TheCardIsRecordedRenderedFalseNamingTheParagraph() => Assert.Fail("pending: T663 — the card is recorded rendered:false naming the paragraph");

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void TheStubReceivedZeroRequestsForThatCard() => Assert.Fail("pending: T663 — the stub received zero requests for that card");
    }

    // AC12 — a timeout is a card
    public sealed class ScenarioATimeoutIsACard
    {
        // Given a stub that never answers and a 1 s timeout
        // When  render runs

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void TheCardIsRecordedRenderedFalseWithReasonTimeout() => Assert.Fail("pending: T663 — the card is recorded rendered:false with reason timeout");
    }
}
