// STORY-545 — Cut the sentence, keep the DJ (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T711)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-10); the named task activates its
// facts, watches them fail, then makes them pass.
// entry point: SentenceTrimmer.Trim (pure; reached via LlmCopyWriter.WriteAsync, see the _Writer file)

using GenWave.Core.Domain;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureCutTheSentenceKeepTheDJTrimmer
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the repeated sentence goes
    public sealed class ScenarioTheRepeatedSentenceGoes
    {
        // Given a three-sentence copy whose second sentence contains a hit phrase
        // When  the trimmer runs

        readonly TrimResult result = new SentenceTrimmer().Trim(
            "Good evening, you're on GenWave. Here it comes, the song that never ends! Stay close, the next one is a ripper.",
            [new RepetitionHit("the song that never ends")]);

        [Fact]
        public void TheResultIsTheFirstAndThirdSentencesInOrder() =>
            Assert.Equal("Good evening, you're on GenWave. Stay close, the next one is a ripper.", result.Text);
    }

    // AC2 — two hits, two sentences
    public sealed class ScenarioTwoHitsTwoSentences
    {
        // Given a four-sentence copy with hits in the first and last sentences
        // When  the trimmer runs

        readonly TrimResult result = new SentenceTrimmer().Trim(
            "Another night, another Friday groove. That was Boards of Canada. Up next, something warm and slow… Keep it locked, never stop listening!",
            [new RepetitionHit("another night another friday groove"), new RepetitionHit("never stop listening")]);

        [Fact]
        public void TheResultIsTheTwoMiddleSentences() =>
            Assert.Equal("That was Boards of Canada. Up next, something warm and slow…", result.Text);
    }

    // AC3 — the trimmer says how many it dropped
    public sealed class ScenarioTheTrimmerSaysHowManyItDropped
    {
        // Given a copy with one hit sentence
        // When  the trimmer runs

        readonly TrimResult result = new SentenceTrimmer().Trim(
            "That was a lovely one. Rain on the window, as always. Next up, a slow burner.",
            [new RepetitionHit("rain on the window")]);

        [Fact]
        public void TheDroppedCountIs1() => Assert.Equal(1, result.DroppedCount);
    }

    // Review round 2 — the boundary rule is the shared one (SPEC F123.2), and a hit takes every sentence it touches

    public sealed class ScenarioAnAbbreviationPeriodIsNotASentenceEnd
    {
        // Given a copy whose hit sentence ends right after "Dr. Dre"
        // When  the trimmer runs

        readonly TrimResult result = new SentenceTrimmer().Trim(
            "Here's Dr. Dre with the song that never ends. Stay close, the next one is a ripper tonight.",
            [new RepetitionHit("the song that never ends")]);

        [Fact]
        public void NoStrandedAbbreviationIsLeftBehind() =>
            Assert.Equal("Stay close, the next one is a ripper tonight.", result.Text);
    }

    public sealed class ScenarioADecimalPointIsNotASentenceEnd
    {
        // Given a copy with "33.3" in a kept sentence
        // When  the trimmer runs

        readonly TrimResult result = new SentenceTrimmer().Trim(
            "Spinning at 33.3 tonight. Rain on the window, as always.",
            [new RepetitionHit("rain on the window")]);

        [Fact]
        public void TheDecimalStaysInsideItsSentence() => Assert.Equal("Spinning at 33.3 tonight.", result.Text);
    }

    public sealed class ScenarioAClosingQuoteStaysWithItsSentence
    {
        // Given a hit sentence that ends inside a quotation
        // When  the trimmer runs

        readonly TrimResult result = new SentenceTrimmer().Trim(
            "She said \"rain on the window.\" Next up, a slow burner.",
            [new RepetitionHit("rain on the window")]);

        [Fact]
        public void TheKeptTextHasNoLeadingQuote() => Assert.Equal("Next up, a slow burner.", result.Text);
    }

    public sealed class ScenarioAHitCrossingASentenceBoundary
    {
        // Given a hit phrase whose words run across a sentence boundary
        // When  the trimmer runs

        readonly TrimResult result = new SentenceTrimmer().Trim(
            "Good evening. That was the song that never. Ends the way it began. Stay close.",
            [new RepetitionHit("song that never ends")]);

        [Fact]
        public void BothSentencesAreDropped() => Assert.Equal(2, result.DroppedCount);
    }
}
