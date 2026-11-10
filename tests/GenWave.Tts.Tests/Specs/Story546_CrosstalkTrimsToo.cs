// STORY-546 — Crosstalk trims too (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T715)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-10); the named task activates its
// facts, watches them fail, then makes them pass.
// entry point: CrosstalkScriptWriter with a two-speaker fake LLM

namespace GenWave.Tts.Tests.Specs;

public static class FeatureCrosstalkTrimsToo
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — a line keeps its fresh sentence
    public sealed class ScenarioALineKeepsItsFreshSentence
    {
        // Given speaker A's line has two sentences and the first repeats A's history
        // When  the exchange is assembled

        [Fact(Skip = "pending: T715 — see docs/PLAN.md")]
        public void AsLineIsItsSecondSentence() => Assert.Fail("pending: T715 — A's line is its second sentence");

        [Fact(Skip = "pending: T715 — see docs/PLAN.md")]
        public void TheLLMWasCalledExactlyOnce() => Assert.Fail("pending: T715 — the LLM was called exactly once");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC2 — an empty line goes to the re-ask
    public sealed class ScenarioAnEmptyLineGoesToTheReAsk
    {
        // Given speaker A's line is one repeated sentence
        // When  the exchange is written

        [Fact(Skip = "pending: T715 — see docs/PLAN.md")]
        public void TheExchangeIsAskedAgainOnce() => Assert.Fail("pending: T715 — the exchange is asked again once");
    }

    // AC3 — still empty means dropped
    public sealed class ScenarioStillEmptyMeansDropped
    {
        // Given speaker A's line is all repeats after the re-ask
        // When  the exchange is assembled

        [Fact(Skip = "pending: T715 — see docs/PLAN.md")]
        public void AsLineIsAbsentAndBsLinesAreIntact() => Assert.Fail("pending: T715 — A's line is absent and B's lines are intact");
    }
}
