// STORY-298 — One fact, sometimes, in the patter (F107.5)

namespace GenWave.Tts.Tests.Specs;

public static class FeatureOneFactPatterLane
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioADueFactAppearsOnce
    {
        [Fact(Skip = "Pending T225 — see docs/PLAN.md")]
        public void ExactlyOneContextLineIsPresentWhenAFactIsDue()
        {
            // BuildUserContent with one fresh, cadence-due patter fact ⇒ exactly one
            // context line in the output; a second due provider does NOT add a second line.
            // Assert.Equal(1, contextLineCount);
            Assert.Fail("pending T225");
        }
    }

    public sealed class ScenarioOtherwiseByteIdentical
    {
        [Fact(Skip = "Pending T225 — see docs/PLAN.md")]
        public void NoDueFactMeansTheGoldenPromptByteForByte()
        {
            // The epic's risk-#1 guard: with no due fact, BuildUserContent output equals
            // the pre-F107 golden capture exactly.
            // Assert.Equal(goldenPrompt, actualPrompt);
            Assert.Fail("pending T225");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioStaleFactsNeverSpeak
    {
        [Fact(Skip = "Pending T225 — see docs/PLAN.md")]
        public void AFactPastFreshUntilProducesNoLine()
        {
            // Cadence due but FreshUntil elapsed ⇒ no context line (byte-identical prompt).
            // Assert.Equal(goldenPrompt, actualPrompt);
            Assert.Fail("pending T225");
        }
    }
}
