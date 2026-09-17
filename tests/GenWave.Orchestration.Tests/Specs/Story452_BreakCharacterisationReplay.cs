// STORY-452 — The break characterisation replay (gh-#401 · SPEC F185 · PLAN T515, T522)
//
// BDD specification — xUnit. FROZEN for gh-#401: once T515 pins AC1–AC4 against the unsplit code, no PR inside the epic may change an
// assertion in this file. T522 (PR-3) may only un-skip AC6 and add the trace table. One script, served unit by
// unit through OrchestratorBuilder on a FakeTimeProvider: back-announce + lead-in on, station-id every 3
// units, ad every 4, two pending announcements, a vendable crosstalk, a due context segment, time/date
// on-time / late / expired, a show boundary with a straddling track, a ceremony-only unit, one null render,
// one over-budget render.
//
// RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body — remove the Skip only in the
// task that makes it green. The builder comments name the arrange each scenario needs.

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBreakCharacterisationReplay
{
    const string Pending = "pending: T515 — the F185.1 script pinned against the unsplit Orchestrator (STORY-452)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheScriptServedEndToEnd
    {
        // Given: the F185.1 script through the builder, every unit served

        /// <summary>AC1 — media ids per unit equal the pinned table</summary>
        [Fact(Skip = Pending)]
        public void BuffersTheUnitsInThePinnedOrder() => Assert.Fail(Pending);

        /// <summary>AC2 — DjName per buffered item equals the pinned table</summary>
        [Fact(Skip = Pending)]
        public void StampsThePinnedDjNames() => Assert.Fail(Pending);

        /// <summary>AC3 — event kinds in order equal the pinned list</summary>
        [Fact(Skip = Pending)]
        public void PublishesThePinnedEventKinds() => Assert.Fail(Pending);

        /// <summary>AC4 — WARN+ messages equal the pinned list</summary>
        [Fact(Skip = Pending)]
        public void LogsThePinnedWarnings() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheFileHeader
    {
        // Given: this file's leading comment

        /// <summary>AC5 — the header names gh-#401 and the one PR that may add an assertion</summary>
        [Fact(Skip = Pending)]
        public void DeclaresTheFreeze() => Assert.Fail(Pending);
    }

    public sealed class ScenarioThePlanTraces
    {
        // Given: the same run once BreakPlan exists (T522)

        /// <summary>AC6 — ToTrace() per unit equals the pinned table</summary>
        [Fact(Skip = Pending)]
        public void MatchThePinnedTraceTable() => Assert.Fail(Pending);
    }
}
