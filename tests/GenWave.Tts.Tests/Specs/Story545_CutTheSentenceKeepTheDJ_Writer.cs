// STORY-545 — Cut the sentence, keep the DJ (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T714)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-10); the named task activates its
// facts, watches them fail, then makes them pass.
// entry point: LlmCopyWriter.WriteAsync / WritePreviewAsync with a fake LLM, fake utterance store and fake booth log

namespace GenWave.Tts.Tests.Specs;

public static class FeatureCutTheSentenceKeepTheDJWriter
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC4 — a trimmed line airs with no second call
    public sealed class ScenarioATrimmedLineAirsWithNoSecondCall
    {
        // Given a fake LLM whose copy has one repeated sentence and 12 other words
        // When  WriteAsync runs on air

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheStoredUtteranceIsTheCopyWithoutThatSentence() => Assert.Fail("pending: T714 — the stored utterance is the copy without that sentence");

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheLLMWasCalledExactlyOnce() => Assert.Fail("pending: T714 — the LLM was called exactly once");

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheUtteranceHasFallbackFalse() => Assert.Fail("pending: T714 — the utterance has fallback = false");
    }

    // AC5 — the booth row says trimmed
    public sealed class ScenarioTheBoothRowSaysTrimmed
    {
        // Given that trimmed break
        // When  the booth log is read

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheRepeatCaughtRowsSummaryEndsWithTrimmed() => Assert.Fail("pending: T714 — the repeat-caught row's summary ends with \"trimmed\"");
    }

    // AC6 — too little left means one re-ask
    public sealed class ScenarioTooLittleLeftMeansOneReAsk
    {
        // Given a fake LLM whose first copy is one repeated sentence and whose second is fresh
        // When  WriteAsync runs on air

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheSecondCopyIsStoredAndRendered() => Assert.Fail("pending: T714 — the second copy is stored and rendered");

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheRepeatCaughtRowsSummaryEndsWithReAsked() => Assert.Fail("pending: T714 — the repeat-caught row's summary ends with \"re-asked\"");
    }

    // AC7 — the re-ask is trimmed too
    public sealed class ScenarioTheReAskIsTrimmedToo
    {
        // Given a fake LLM whose first copy is all repeats and whose second has one repeated sentence and 12 other words
        // When  WriteAsync runs on air

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheStoredUtteranceIsTheSecondCopyWithoutThatSentence() => Assert.Fail("pending: T714 — the stored utterance is the second copy without that sentence");

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheLLMWasCalledExactlyTwice() => Assert.Fail("pending: T714 — the LLM was called exactly twice");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC9 — nothing usable twice means the template
    public sealed class ScenarioNothingUsableTwiceMeansTheTemplate
    {
        // Given a fake LLM that returns the same all-repeat copy twice
        // When  WriteAsync runs on air

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheTemplateFallbackIsRendered() => Assert.Fail("pending: T714 — the template fallback is rendered");

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheRepeatCaughtRowsSummaryEndsWithTemplate() => Assert.Fail("pending: T714 — the repeat-caught row's summary ends with \"template\"");

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheLLMWasCalledExactlyTwice() => Assert.Fail("pending: T714 — the LLM was called exactly twice");
    }

    // AC10 — seven words is too little
    public sealed class ScenarioSevenWordsIsTooLittle
    {
        // Given a copy whose remainder after the trim is one seven-word sentence
        // When  WriteAsync runs on air

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheLLMIsAskedAgain() => Assert.Fail("pending: T714 — the LLM is asked again");
    }

    // AC11 — a remainder the truth gate rejects is not aired
    public sealed class ScenarioARemainderTheTruthGateRejectsIsNotAired
    {
        // Given a trimmed remainder that fails the truth gate
        // When  WriteAsync runs on air

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheLLMIsAskedAgain() => Assert.Fail("pending: T714 — the LLM is asked again");
    }

    // AC12 — preview never trims
    public sealed class ScenarioPreviewNeverTrims
    {
        // Given a preview request whose copy repeats
        // When  WritePreviewAsync runs

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheCopyComesBackWhole() => Assert.Fail("pending: T714 — the copy comes back whole");
    }

    // AC13 — the trim may not lose the song
    public sealed class ScenarioTheTrimMayNotLoseTheSong
    {
        // Given the copy "That was ABBA's Knowing Me, Knowing You, nineteen seventy-six, naturally. What a record. Stay right here, there is plenty more coming up tonight." with "naturally" over quota
        // When  WriteAsync runs on air

        [Fact(Skip = "pending: T714 — see docs/PLAN.md")]
        public void TheLLMIsAskedAgain() => Assert.Fail("pending: T714 — the LLM is asked again");
    }
}
