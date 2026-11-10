// STORY-545 — Cut the sentence, keep the DJ (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T711)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-10); the named task activates its
// facts, watches them fail, then makes them pass.
// entry point: SentenceTrimmer.Trim (pure; reached via LlmCopyWriter.WriteAsync, see the _Writer file)

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

        [Fact(Skip = "pending: T711 — see docs/PLAN.md")]
        public void TheResultIsTheFirstAndThirdSentencesInOrder() => Assert.Fail("pending: T711 — the result is the first and third sentences, in order");
    }

    // AC2 — two hits, two sentences
    public sealed class ScenarioTwoHitsTwoSentences
    {
        // Given a four-sentence copy with hits in the first and last sentences
        // When  the trimmer runs

        [Fact(Skip = "pending: T711 — see docs/PLAN.md")]
        public void TheResultIsTheTwoMiddleSentences() => Assert.Fail("pending: T711 — the result is the two middle sentences");
    }

    // AC3 — the trimmer says how many it dropped
    public sealed class ScenarioTheTrimmerSaysHowManyItDropped
    {
        // Given a copy with one hit sentence
        // When  the trimmer runs

        [Fact(Skip = "pending: T711 — see docs/PLAN.md")]
        public void TheDroppedCountIs1() => Assert.Fail("pending: T711 — the dropped count is 1");
    }
}
