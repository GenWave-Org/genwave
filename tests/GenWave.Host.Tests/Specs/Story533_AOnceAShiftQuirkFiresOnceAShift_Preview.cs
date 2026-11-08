// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218 · PLAN T686)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans four files; see tests/GenWave.Core.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Card.cs for the map.

namespace GenWave.Host.Tests.Specs;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftPreview
{
    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — preview never spends
    // entry point: WebApplicationFactory<Program> admin persona prompt-preview endpoint
    public sealed class ScenarioPreviewNeverSpends
    {
        // Given the admin preview endpoint
        // When  it builds a prompt for a persona with a maxPerShift quirk

        [Fact(Skip = "pending: T686 — see docs/PLAN.md")]
        public void NoSpendRowIsWritten() => Assert.Fail("pending: T686 — no spend row is written");
    }
}
