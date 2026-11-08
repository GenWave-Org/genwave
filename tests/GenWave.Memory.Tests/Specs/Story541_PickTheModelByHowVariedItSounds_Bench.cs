// STORY-541 — Pick the model by how varied it sounds (gh-#293 gh-#920 · SPEC F218 · PLAN T695)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans three files by seam: AC1/AC3/AC5 (T695, bench process + scoring) here; AC2 (T695, real prompt) in
// tests/GenWave.Tts.Tests/Specs/Story541_PickTheModelByHowVariedItSounds_Prompt.cs; AC4 (T697, defaults) in
// tests/GenWave.Host.Tests/Specs/Story541_PickTheModelByHowVariedItSounds_Defaults.cs.

namespace GenWave.Memory.Tests.Specs;

public static class FeaturePickTheModelByHowVariedItSoundsBench
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the report
    // entry point: the `dotnet run --project tools/PatterBench` process against a fake endpoint
    public sealed class ScenarioTheReport
    {
        // Given a fake endpoint returning canned copies and --models a,b --personas flip,sal -n 5
        // When  the bench runs

        [Fact(Skip = "pending: T695 — see docs/PLAN.md")]
        public void ItWritesAMarkdownReportWithOneRowPerModelPersona() => Assert.Fail("pending: T695 — it writes a Markdown report with one row per model × persona");

        [Fact(Skip = "pending: T695 — see docs/PLAN.md")]
        public void EachRowHasTheSixMetrics() => Assert.Fail("pending: T695 — each row has the six metrics");
    }

    // AC3 — the catchphrase rate
    public sealed class ScenarioTheCatchphraseRate
    {
        // Given canned copies where 3 of 5 contain "banger, log it"
        // When  scored for a persona whose rule names that phrase

        [Fact(Skip = "pending: T695 — see docs/PLAN.md")]
        public void TheCatchphraseRateIs06() => Assert.Fail("pending: T695 — the catchphrase rate is 0.6");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC5 — a dead endpoint is a clear failure
    // entry point: the `dotnet run --project tools/PatterBench` process
    public sealed class ScenarioADeadEndpointIsAClearFailure
    {
        // Given an endpoint that refuses connections
        // When  the bench runs

        [Fact(Skip = "pending: T695 — see docs/PLAN.md")]
        public void ItExitsNonZeroNamingTheEndpointWithNoPartialReport() => Assert.Fail("pending: T695 — it exits non-zero naming the endpoint, with no partial report");
    }
}
