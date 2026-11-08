// STORY-534 — The DJ does not say that again (gh-#293 gh-#920 · SPEC F218 · PLAN T692 T693)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans two files; see tests/GenWave.Memory.Tests/Specs/Story534_TheDjDoesNotSayThatAgain_Check.cs for the map.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureTheDjDoesNotSayThatAgainWriter
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — recent lines reach the prompt
    // entry point: LlmCopyWriter.WriteAsync (prompt posted to the LLM)
    public sealed class ScenarioRecentLinesReachThePrompt
    {
        // Given a persona with 8 stored utterances and a non-zero memory budget
        // When  the prompt is built

        [Fact(Skip = "pending: T692 — see docs/PLAN.md")]
        public void ItContainsARecentLinesDoNotReuseTheirPhrasingBlock() => Assert.Fail("pending: T692 — it contains a \"Recent lines, do not reuse their phrasing\" block");

        [Fact(Skip = "pending: T692 — see docs/PLAN.md")]
        public void TheBlockListsThe6NewestUtterancesEachTrimmedTo160Characters() => Assert.Fail("pending: T692 — the block lists the 6 newest utterances, each trimmed to 160 characters");
    }

    // AC5 — one re-ask, then fresh copy airs
    // entry point: LlmCopyWriter.WriteAsync
    public sealed class ScenarioOneReAskThenFreshCopyAirs
    {
        // Given a fake LLM whose first copy repeats and second does not
        // When  WriteAsync runs

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheSecondCopyIsStoredAndRendered() => Assert.Fail("pending: T693 — the second copy is stored and rendered");

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheLLMWasCalledExactlyTwice() => Assert.Fail("pending: T693 — the LLM was called exactly twice");

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheReAskPromptNamesTheOffendingPhraseAfterDoNotSay() => Assert.Fail("pending: T693 — the re-ask prompt names the offending phrase after \"do not say:\"");
    }

    // AC6 — the catch is logged
    // entry point: BoothLogWriter via the event bus (repeat-caught)
    public sealed class ScenarioTheCatchIsLogged
    {
        // Given a caught repeat
        // When  the re-ask is issued

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void ABoothLogRowKindRepeatCaughtNamesThePersonaAndThePhrase() => Assert.Fail("pending: T693 — a booth-log row kind repeat-caught names the persona and the phrase");

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheLLMCallRecorderCountsOneCallWithCauseRepeat() => Assert.Fail("pending: T693 — the LLM call recorder counts one call with cause repeat");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — two hits means the template
    public sealed class ScenarioTwoHitsMeansTheTemplate
    {
        // Given a fake LLM that returns the same repeating copy twice
        // When  WriteAsync runs

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheTemplateFallbackIsRendered() => Assert.Fail("pending: T693 — the template fallback is rendered");

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheStoredUtteranceHasFallbackTrue() => Assert.Fail("pending: T693 — the stored utterance has fallback = true");

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheLLMWasCalledExactlyTwice() => Assert.Fail("pending: T693 — the LLM was called exactly twice");
    }

    // AC8 — memory off, block off
    public sealed class ScenarioMemoryOffBlockOff
    {
        // Given a zero memory budget
        // When  the prompt is built

        [Fact(Skip = "pending: T692 — see docs/PLAN.md")]
        public void NoRecentLinesBlockIsPresent() => Assert.Fail("pending: T692 — no recent-lines block is present");
    }

    // AC8 — memory off, the post-check still runs
    // entry point: LlmCopyWriter.WriteAsync
    public sealed class ScenarioMemoryOffThePostCheckStillRuns
    {
        // Given a zero memory budget
        // When  WriteAsync runs

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void ThePostCheckStillRuns() => Assert.Fail("pending: T693 — the post-check still runs");
    }
}
