// STORY-455 — BreakPlanner decides the break (gh-#401 · SPEC F188 · PLAN T521–T523)
//
// BDD specification — xUnit. AC1–AC4 drive BreakPlanner directly with recording/throwing/counting fakes; AC5 is the STORY-452 replay
// through the Orchestrator that consumes the plan; AC6 reflects the deleted methods (T522).
//
// RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body — remove the Skip only in the
// task that makes it green. The builder comments name the arrange each scenario needs.

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBreakPlanner
{
    const string Pending = "pending: T521 — BreakPlanner.PlanAsync performs today's steps and drains in today's order (STORY-455)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheScriptPlanned
    {
        // Given: PlanAsync per unit of the F185.1 script

        /// <summary>AC1 — slot kinds in order equal the trace table</summary>
        [Fact(Skip = Pending)]
        public void YieldsThePinnedSlotKinds() => Assert.Fail(Pending);
    }

    public sealed class ScenarioThrowingRenderFakes
    {
        // Given: ITtsSegmentSource, IVerbatimSegmentRenderer and the copy writer throw when called

        /// <summary>AC2 — the plan phase is render-free</summary>
        [Fact(Skip = Pending)]
        public void NoFakeWasCalled() => Assert.Fail(Pending);
    }

    public sealed class ScenarioRecordingSideEffectFakes
    {
        // Given: crosstalk, announcements, deferral queue, catalog, ad vend record their calls

        /// <summary>AC3 — MarkVended, claim, StationId enqueue, Ad enqueue, drain, pool lookup, vend</summary>
        [Fact(Skip = Pending)]
        public void RecordsTodaysOrder() => Assert.Fail(Pending);
    }

    public sealed class ScenarioCountingBudgetProviders
    {
        // Given: budget providers count their reads

        /// <summary>AC4 — </summary>
        [Fact(Skip = Pending)]
        public void ReadsEachOnce() => Assert.Fail(Pending);

        /// <summary>AC4 — </summary>
        [Fact(Skip = Pending)]
        public void CarriesTheRenderBudgetOnThePlan() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheOrchestratorRidesThePlan
    {
        // Given: the STORY-452 replay after T522

        /// <summary>AC5 — </summary>
        [Fact(Skip = Pending)]
        public void KeepsEveryFrozenAssertionGreen() => Assert.Fail(Pending);

        /// <summary>AC5 — </summary>
        [Fact(Skip = Pending)]
        public void MatchesThePinnedTraces() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheOldPathReflected
    {
        // Given: typeof(Orchestrator) non-public methods

        /// <summary>AC6 — </summary>
        [Fact(Skip = Pending)]
        public void HasNoEnqueuePatterAsync() => Assert.Fail(Pending);

        /// <summary>AC6 — BuildStationIdRequest, BuildAdRequest, BuildHandoffRequest, BuildTimeDateRequest, BuildContextSegmentRequestAsync</summary>
        [Fact(Skip = Pending)]
        public void HasNoBuildRequestMethods() => Assert.Fail(Pending);
    }
}
