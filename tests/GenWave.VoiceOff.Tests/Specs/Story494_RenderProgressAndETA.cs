// STORY-494 — render progress and ETA (gh-#614 · SPEC brief extra · PLAN T612)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureRenderProgressAndETA
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — progress is printed
    public sealed class ScenarioProgressIsPrinted
    {
        // Given a run of 4 clips with 3 done at a median of 10 s per clip
        // When  the 4th clip starts

        [Fact(Skip = "pending: T612 — see docs/PLAN.md")]
        public void StdoutShowsClip441MinLeft() => Assert.Fail("pending: T612 — stdout shows 'clip 4/4 · ~1 min left'");
    }

    // AC2 — no ETA before the first clip
    public sealed class ScenarioNoETABeforeTheFirstClip
    {
        // Given a run with no clip done
        // When  progress prints

        [Fact(Skip = "pending: T612 — see docs/PLAN.md")]
        public void ItShowsClip1NWithNoETA() => Assert.Fail("pending: T612 — it shows 'clip 1/N' with no ETA");
    }
}
