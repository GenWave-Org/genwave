// STORY-495 — `render --dry-run` (gh-#614 · SPEC brief extra · PLAN T613)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureRenderDryRun
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — clip count
    public sealed class ScenarioClipCount
    {
        // Given 2 attempted engines, 2 lanes, 10 lines
        // When  `render --dry-run` runs

        [Fact(Skip = "pending: T613 — see docs/PLAN.md")]
        public void ItPrints40Clips() => Assert.Fail("pending: T613 — it prints 40 clips");
    }

    // AC2 — ETA from the last run
    public sealed class ScenarioETAFromTheLastRun
    {
        // Given a previous timing.jsonl with median wall 5 s
        // When  `render --dry-run` runs for 40 clips

        [Fact(Skip = "pending: T613 — see docs/PLAN.md")]
        public void ItPrints4Min() => Assert.Fail("pending: T613 — it prints '~4 min'");
    }

    // AC3 — no engine is touched
    public sealed class ScenarioNoEngineIsTouched
    {
        // Given a stub endpoint counting requests
        // When  `render --dry-run` runs

        [Fact(Skip = "pending: T613 — see docs/PLAN.md")]
        public void TheStubSawZeroRequests() => Assert.Fail("pending: T613 — the stub saw zero requests");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC4 — no prior run
    public sealed class ScenarioNoPriorRun
    {
        // Given no previous timing.jsonl
        // When  `render --dry-run` runs

        [Fact(Skip = "pending: T613 — see docs/PLAN.md")]
        public void ItPrintsETAUnknownNoPreviousRun() => Assert.Fail("pending: T613 — it prints 'ETA unknown (no previous run)'");
    }
}
