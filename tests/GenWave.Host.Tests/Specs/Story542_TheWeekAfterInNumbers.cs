// STORY-542 — The week after, in numbers (gh-#293 gh-#920 · SPEC F218 · PLAN T708)
//
// BDD specification — xUnit. Views and Status page; WebApplicationFactory plus the Postgres fixture.
// Every fact is pending at plan time (2026-11-08); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheWeekAfterInNumbers
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — Catchphrases per shift
    public sealed class ScenarioCatchphrasesPerShift
    {
        // entry point: station.v_catchphrase_per_shift via the Status endpoint (WebApplicationFactory<Program>)
        // Given utterances where Flip's "banger, log it" appears twice in one shift and once in another
        // When  the view is queried

        [Fact(Skip = "pending: T708 — see docs/PLAN.md")]
        public void ItReports2ForTheFirstShiftKey() => Assert.Fail("pending: T708 — it reports 2 for the first shift key");

        [Fact(Skip = "pending: T708 — see docs/PLAN.md")]
        public void ItReports1ForTheSecond() => Assert.Fail("pending: T708 — it reports 1 for the second");
    }

    // AC2 — Catches and fallbacks
    public sealed class ScenarioCatchesAndFallbacks
    {
        // Given 3 repeat-caught rows and 1 fallback utterance for Flip in 7 days
        // When  the view is queried

        [Fact(Skip = "pending: T708 — see docs/PLAN.md")]
        public void ItReportsCatches3() => Assert.Fail("pending: T708 — it reports catches 3");

        [Fact(Skip = "pending: T708 — see docs/PLAN.md")]
        public void ItReportsFallbacks1() => Assert.Fail("pending: T708 — it reports fallbacks 1");
    }

    // AC3 — On the Status page
    public sealed class ScenarioOnTheStatusPage
    {
        // Given the views have rows
        // When  the Status page loads

        [Fact(Skip = "pending: T708 — see docs/PLAN.md")]
        public void ARepeatsPanelShowsThePerDJNumbersInPlainWords() => Assert.Fail("pending: T708 — a \"Repeats\" panel shows the per-DJ numbers in plain words");
    }
}
