// STORY-550 — Every ask leaves a record, the word rule half (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. Pure CallbackWordRule facts (AC3, AC9) plus the planner's offer/settle facts over the in-memory store (AC1, AC2).
// Every fact is pending at plan time (2026-11-16); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Memory.Tests.Specs;

public static class FeatureEveryAskLeavesARecordWordRule
{
    // AC1 — written before the model call
    public sealed class ScenarioWrittenBeforeTheModelCall
    {
        // Given a callback break
        // When  the planner returns the ask

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void AnOfferExistsWithTheMemoryTheDJAndUsedFalse() => Assert.Fail("pending: T734 — an offer exists with the memory the DJ and used false");
    }

    // AC2 — settled with the line
    public sealed class ScenarioSettledWithTheLine
    {
        // Given the aired line "like Daisy chasin' her tail"

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void TheOfferHasTheUtteranceIdUsedTrueAndMatchedWordDaisy() => Assert.Fail("pending: T734 — the offer has the utterance id used true and matched word daisy");
    }

    // AC3 — the word rule (table)
    public sealed class ScenarioTheWordRule
    {

        [Fact(Skip = "pending: T731 — see docs/PLAN.md")]
        public void ADogNamedDaisyIsUsedByLikeDaisyChasinHerTail() => Assert.Fail("pending: T731 — a dog named Daisy is used by like Daisy chasin her tail");

        [Fact(Skip = "pending: T731 — see docs/PLAN.md")]
        public void TheDJsOwnNameMaxxieIsNotAMatch() => Assert.Fail("pending: T731 — the DJs own name Maxxie is not a match");

        [Fact(Skip = "pending: T731 — see docs/PLAN.md")]
        public void ACardWordBallroomIsNotAMatch() => Assert.Fail("pending: T731 — a card word ballroom is not a match");

        [Fact(Skip = "pending: T731 — see docs/PLAN.md")]
        public void AnotherDJsNameProfessorBIsNotAMatch() => Assert.Fail("pending: T731 — another DJs name Professor B is not a match");

        [Fact(Skip = "pending: T731 — see docs/PLAN.md")]
        public void TheTrackTitleButterflyIsNotAMatch() => Assert.Fail("pending: T731 — the track title Butterfly is not a match");

        [Fact(Skip = "pending: T731 — see docs/PLAN.md")]
        public void RestoresMatchesRestoring() => Assert.Fail("pending: T731 — restores matches restoring");
    }

    // AC9 — a short word is not a match (sad path)
    public sealed class ScenarioAShortWordIsNotAMatch
    {
        // Given "has a cat" and "the cat's out of the bag"

        [Fact(Skip = "pending: T731 — see docs/PLAN.md")]
        public void AThreeLetterWordDoesNotCount() => Assert.Fail("pending: T731 — a three letter word does not count");
    }
}
