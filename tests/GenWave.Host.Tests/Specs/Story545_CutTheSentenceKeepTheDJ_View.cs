// STORY-545 — Cut the sentence, keep the DJ (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T708)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-10); the named task activates its
// facts, watches them fail, then makes them pass.
// entry point: station.v_repeat_catches_7d over real Postgres (Integration)

namespace GenWave.Host.Tests.Specs;

public static class FeatureCutTheSentenceKeepTheDJView
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC8 — trims are counted apart from fallbacks
    public sealed class ScenarioTrimsAreCountedApartFromFallbacks
    {
        // Given 2 repeat-caught rows ending "trimmed" and 1 ending "template" for one DJ in 7 days
        // When  the repeat view is queried

        [Fact(Skip = "pending: T708 — see docs/PLAN.md")]
        public void ItReportsTrims2AndFallbacks1() => Assert.Fail("pending: T708 — it reports trims 2 and fallbacks 1");
    }
}
