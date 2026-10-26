// STORY-524 — Same text in: normalised, cue-mapped, words law (gh-#614 · SPEC F217 · PLAN T662)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-26); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureSameTextIn
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the phone paragraph is normalised
    public sealed class ScenarioThePhoneParagraphIsNormalised
    {
        // Given the paragraph with 1-800-555-0199 and a card with no overrides
        // When  the card text is prepared

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void NoASCIIDigitRemainsInTheText() => Assert.Fail("pending: T662 — no ASCII digit remains in the text");
    }

    // AC2 — a cue becomes the card's tag
    public sealed class ScenarioACueBecomesTheCardSTag
    {
        // Given a card mapping laugh to [laugh]
        // When  the laugh paragraph is prepared

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void LaughStandsWhereLaughWas() => Assert.Fail("pending: T662 — [laugh] stands where {laugh} was");
    }

    // AC3 — a cue becomes a written stand-in
    public sealed class ScenarioACueBecomesAWrittenStandIn
    {
        // Given a card mapping um to the word um followed by a comma
        // When  the um paragraph is prepared

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void TheStandInStandsWhereUmWas() => Assert.Fail("pending: T662 — the stand-in stands where {um} was");
    }

    // AC4 — an unmapped cue is dropped and declared
    public sealed class ScenarioAnUnmappedCueIsDroppedAndDeclared
    {
        // Given a card mapping cough to null
        // When  the cough paragraph is prepared

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void NoCoughTokenRemains() => Assert.Fail("pending: T662 — no cough token remains");

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void CoughIsListedInTheCardSCannotFacts() => Assert.Fail("pending: T662 — cough is listed in the card's cannot facts");
    }

    // AC5 — an override wins for its paragraph
    public sealed class ScenarioAnOverrideWinsForItsParagraph
    {
        // Given a card overriding paragraph 3 with the same words and an extra ellipsis
        // When  the card text is prepared

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void Paragraph3EqualsTheOverride() => Assert.Fail("pending: T662 — paragraph 3 equals the override");

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void Paragraph4IsUnchanged() => Assert.Fail("pending: T662 — paragraph 4 is unchanged");
    }

    // AC6 — the as-shipped card speaks the station style
    public sealed class ScenarioTheAsShippedCardSpeaksTheStationStyle
    {
        // Given the asShipped card
        // When  its text is prepared

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void EveryCueIsStripped() => Assert.Fail("pending: T662 — every cue is stripped");

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void TheTextEqualsKokoroSpeechMarkupOverTheNormalisedCueStrippedParagraph() => Assert.Fail("pending: T662 — the text equals KokoroSpeechMarkup over the normalised cue-stripped paragraph");
    }

    // AC7 — the words law passes for tags and punctuation
    public sealed class ScenarioTheWordsLawPassesForTagsAndPunctuation
    {
        // Given a card whose override adds [sigh] and a comma
        // When  the words law runs

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void ItPasses() => Assert.Fail("pending: T662 — it passes");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC8 — a changed word fails the words law
    public sealed class ScenarioAChangedWordFailsTheWordsLaw
    {
        // Given an override replacing kinda with kind of
        // When  the words law runs

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void ItFailsNamingTheParagraphNumber() => Assert.Fail("pending: T662 — it fails naming the paragraph number");
    }

    // AC9 — a dropped word fails the words law
    public sealed class ScenarioADroppedWordFailsTheWordsLaw
    {
        // Given an override missing the word GenWave
        // When  the words law runs

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void ItFailsNamingTheParagraphNumber() => Assert.Fail("pending: T662 — it fails naming the paragraph number");
    }

    // AC10 — an undeclared tag fails the words law
    public sealed class ScenarioAnUndeclaredTagFailsTheWordsLaw
    {
        // Given an override using [gasp] when the card declares only [laugh]
        // When  the words law runs

        [Fact(Skip = "pending: T662 — see docs/PLAN.md")]
        public void ItFailsNamingTheTag() => Assert.Fail("pending: T662 — it fails naming the tag");
    }
}
