// STORY-537 — The air path is not delayed (gh-#293 gh-#920 · SPEC F218 · PLAN T704)
//
// BDD specification — xUnit. AC2 only; the other ACs live in tests/GenWave.Memory.Tests/Specs/Story537_AnInventedLifeThatStaysConsistent.cs.
// Every fact is pending at plan time (2026-11-08); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureTheAirPathIsNotDelayed
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC2 — The air path is not delayed
    public sealed class ScenarioTheAirPathIsNotDelayed
    {
        // entry point: LlmCopyWriter.WriteAsync with a slow ILifeExtractor fake
        // Given an extractor fake that takes 5 seconds
        // When  WriteAsync accepts a copy

        [Fact(Skip = "pending: T704 — see docs/PLAN.md")]
        public void TheRenderIsDispatchedBeforeTheExtractorCompletes() => Assert.Fail("pending: T704 — the render is dispatched before the extractor completes");
    }
}
