// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218 · PLAN T687)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans four files; see tests/GenWave.Core.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Card.cs for the map.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftPrompt
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC5 — the positive line
    // entry point: LlmCopyWriter.WriteAsync (prompt posted to the LLM)
    public sealed class ScenarioThePositiveLine
    {
        // Given the break where the maxPerShift quirk is chosen
        // When  the prompt is built

        [Fact(Skip = "pending: T687 — see docs/PLAN.md")]
        public void ItContainsOneLineOfTheThisIsYourOneOfTheShiftLandItClass() => Assert.Fail("pending: T687 — it contains one line of the \"this is your one … of the shift, land it\" class");

        [Fact(Skip = "pending: T687 — see docs/PLAN.md")]
        public void TheQuirksBlockContainsNoDoNotOrNeverPhrasing() => Assert.Fail("pending: T687 — the quirks block contains no \"do not\" or \"never\" phrasing");
    }
}
