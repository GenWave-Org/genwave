// STORY-550 — Every ask leaves a record, the word rule half (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. Pure CallbackWordRule facts (AC3, AC9) plus the planner's offer/settle facts over the in-memory store (AC1, AC2).
// Every fact is pending at plan time (2026-11-16); the named task activates its facts, watches them fail,
// then makes them pass.

using GenWave.Memory.Tests.Fakes;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureEveryAskLeavesARecordWordRule
{
    static CallbackExclusions Exclusions(params string[] texts) => new(texts);

    // AC1 — written before the model call
    public sealed class ScenarioWrittenBeforeTheModelCall
    {
        // Given a callback break
        // When  the planner returns the ask

        [Fact]
        public async Task AnOfferExistsWithTheMemoryTheDJAndUsedFalse()
        {
            var h = new CallbackPlannerHarness();
            var memory = h.Seed();
            var ask = await h.PlanAsync();
            var offer = Assert.Single(h.Store.Offers);
            Assert.Equal(ask?.OfferId, offer.Id);
            Assert.Equal(memory.Id, offer.MemoryId);
            Assert.Equal(CallbackPlannerHarness.Persona, offer.PersonaId);
            Assert.False(offer.Used);
        }

        [Fact]
        public async Task AFallbackLineLeavesUsedFalse()
        {
            var h = new CallbackPlannerHarness();
            h.Seed();
            var ask = await h.PlanAsync() ?? throw new InvalidOperationException("no ask");
            await h.Planner.SettleAsync(ask, 5, "like Daisy chasin' her tail", true, CancellationToken.None);
            Assert.False(Assert.Single(h.Store.Offers).Used);
        }
    }

    // AC2 — settled with the line
    public sealed class ScenarioSettledWithTheLine
    {
        // Given the aired line "like Daisy chasin' her tail"

        [Fact]
        public async Task TheOfferHasTheUtteranceIdUsedTrueAndMatchedWordDaisy()
        {
            var h = new CallbackPlannerHarness();
            h.Seed("has a dog named Daisy");
            var ask = await h.PlanAsync(exclusions: "Rusty Strings") ?? throw new InvalidOperationException("no ask");
            await h.Planner.SettleAsync(ask, 42, "like Daisy chasin' her tail", false, CancellationToken.None);
            var offer = Assert.Single(h.Store.Offers);
            Assert.Equal(42, offer.UtteranceId);
            Assert.True(offer.Used);
            Assert.Equal(["daisy"], offer.MatchedWords);
        }
    }

    // AC3 — the word rule (table)
    public sealed class ScenarioTheWordRule
    {

        [Fact]
        public void ADogNamedDaisyIsUsedByLikeDaisyChasinHerTail() =>
            Assert.True(CallbackWordRule.Used("has a dog named Daisy", "like Daisy chasin' her tail", Exclusions("Rusty Strings")).Used);

        [Fact]
        public void TheDJsOwnNameMaxxieIsNotAMatch() =>
            Assert.False(CallbackWordRule.Used("has a pet beagle named Maxxie", "Maxxie here with the next one", Exclusions("Maxxie Volt")).Used);

        [Fact]
        public void ACardWordBallroomIsNotAMatch() =>
            Assert.False(CallbackWordRule.Used("learned the ballroom from papa", "welcome back to the ballroom", Exclusions("Big Tony", "ballroom")).Used);

        [Fact]
        public void AnotherDJsNameProfessorBIsNotAMatch() =>
            Assert.False(CallbackWordRule.Used("took over from Professor B", "thanks Professor B", Exclusions("Echo Ellis", "Professor B")).Used);

        [Fact]
        public void TheTrackTitleButterflyIsNotAMatch() =>
            Assert.False(CallbackWordRule.Used("loves old Butterfly records", "here is Butterfly by Crazy Town", Exclusions("Butterfly", "Crazy Town")).Used);

        [Fact]
        public void RestoresMatchesRestoring() =>
            Assert.True(CallbackWordRule.Used("restores tube radios", "spent the weekend restoring a radio", Exclusions("Granny Static")).Used);
    }

    // AC9 — a short word is not a match (sad path)
    public sealed class ScenarioAShortWordIsNotAMatch
    {
        // Given "has a cat" and "the cat's out of the bag"

        [Fact]
        public void AThreeLetterWordDoesNotCount() =>
            Assert.False(CallbackWordRule.Used("has a cat", "the cat's out of the bag", Exclusions("Granny Static")).Used);
    }

    public sealed class ScenarioWordFormsAndDigits
    {
        [Fact]
        public void ASharedYearIsNotAMatch() =>
            Assert.False(CallbackWordRule.Used("won a contest in 1987", "a 1987 classic", Exclusions("Granny Static")).Used);

        [Fact]
        public void HereIsNotHer() =>
            Assert.False(CallbackWordRule.Used("moved here from Ohio", "like Daisy chasin' her tail", Exclusions("Granny Static")).Used);

        [Fact]
        public void NoteIsNotNot() =>
            Assert.False(CallbackWordRule.Used("wrote a note", "it is not that", Exclusions("Granny Static")).Used);

        [Fact]
        public void WeddingMatchesWeddings() =>
            Assert.True(CallbackWordRule.Used("sang at a wedding", "two weddings this weekend", Exclusions("Granny Static")).Used);

        [Theory]
        [InlineData("restores", "restoring")]
        [InlineData("restored", "restore")]
        [InlineData("radios", "radio")]
        [InlineData("walked", "walking")]
        public void FormsOfOneWordAreEqual(string a, string b) => Assert.True(CallbackWordRule.Used(a, b, Exclusions("Granny Static")).Used);

        [Theory]
        [InlineData("here", "her")]
        [InlineData("note", "not")]
        [InlineData("wine", "win")]
        [InlineData("made", "mad")]
        [InlineData("hope", "hop")]
        [InlineData("thing", "the")]
        [InlineData("shed", "she")]
        [InlineData("seed", "see")]
        [InlineData("noted", "not")]
        [InlineData("news", "new")]
        public void UnrelatedWordsAreNotEqual(string a, string b) => Assert.False(CallbackWordRule.Used(a, b, Exclusions("Granny Static")).Used);
    }

    public sealed class ScenarioCommonPatterWordsAreNotAMatch
    {
        [Fact]
        public void TookOverFromCaptainFlipHasNoDistinctiveWord() =>
            Assert.False(CallbackWordRule.HasDistinctiveWord("took over from Captain Flip", Exclusions("Captain Flip")));

        [Fact]
        public void LivesInAHouseWithAViewIsNotUsedByTheHouseOfBlues() =>
            Assert.False(CallbackWordRule.Used("lives in a house with a view of the mountains", "live from the house of blues", Exclusions("Granny Static")).Used);

        [Fact]
        public void LivesInAHouseWithAViewIsUsedByTheMountainsAreOut() =>
            Assert.True(CallbackWordRule.Used("lives in a house with a view of the mountains", "the mountains are out tonight", Exclusions("Granny Static")).Used);
    }

    public sealed class ScenarioTheMatchedWordsAndEligibility
    {
        [Fact]
        public void TheMatchedWordsAreTheSharedDistinctiveWords() =>
            Assert.Equal(["daisy"], CallbackWordRule.Used("has a dog named Daisy", "like Daisy chasin' her tail", Exclusions("Rusty Strings")).Words);

        [Fact]
        public void AMemoryWithADistinctiveWordIsEligible() =>
            Assert.True(CallbackWordRule.HasDistinctiveWord("has a dog named Daisy", Exclusions("Rusty Strings")));

        [Fact]
        public void AMemoryOfOnlyExcludedShortOrStopWordsIsNeverEligible() =>
            Assert.False(CallbackWordRule.HasDistinctiveWord("has a cat in the Ballroom", Exclusions("ballrooms")));

        [Fact]
        public void ExclusionsMatchTheirPluralForms() =>
            Assert.False(CallbackWordRule.Used("learned the ballrooms", "the ballroom", Exclusions("ballroom")).Used);
    }
}
