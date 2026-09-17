// STORY-451 — One construction path (gh-#401 · SPEC F184 · PLAN T510–T514)
//
// BDD specification — xUnit. AC1–AC3 drive OrchestratorBuilder (tests/GenWave.TestSupport); AC6/AC7 drive AddOrchestration through a
// ServiceCollection; AC9 is the recount. AC4/AC5/AC8 are pins in Architecture.Tests (Story451_ConstructionPins).
//
// RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body — remove the Skip only in the
// task that makes it green. The builder comments name the arrange each scenario needs.

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureOneConstructionPath
{
    const string Pending = "pending: T511 — OrchestratorBuilder in tests/GenWave.TestSupport builds the chain (STORY-451)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheBuilderWithDefaults
    {
        // Given: new OrchestratorBuilder().Build()

        /// <summary>AC1 — the chain record carries the Orchestrator</summary>
        [Fact(Skip = Pending)]
        public void ExposesTheOrchestrator() => Assert.Fail(Pending);

        /// <summary>AC1 — the deferral queue is the one the Orchestrator was built with</summary>
        [Fact(Skip = Pending)]
        public void ExposesTheDeferralQueue() => Assert.Fail(Pending);

        /// <summary>AC1 — a FakeTimeProvider drives the chain</summary>
        [Fact(Skip = Pending)]
        public void ExposesTheFakeClock() => Assert.Fail(Pending);

        /// <summary>AC1 — the tts fake is reachable for assertions</summary>
        [Fact(Skip = Pending)]
        public void ExposesTheTtsFake() => Assert.Fail(Pending);

        /// <summary>AC1 — the capturing event sink is reachable</summary>
        [Fact(Skip = Pending)]
        public void ExposesTheEventSink() => Assert.Fail(Pending);

        /// <summary>AC1 — the fake catalog is reachable</summary>
        [Fact(Skip = Pending)]
        public void ExposesTheCatalog() => Assert.Fail(Pending);

        /// <summary>AC1 — the estimator fake is reachable</summary>
        [Fact(Skip = Pending)]
        public void ExposesThePatterEstimator() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAnOverriddenAdSpotVend
    {
        // Given: builder.WithAdSpotVend(fake) and ad cadence every unit; two units served

        /// <summary>AC2 — the seam override reaches the Orchestrator</summary>
        [Fact(Skip = Pending)]
        public void TheFakeReceivedOneVend() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheHarnessRidesTheBuilder
    {
        // Given: ProductionChainHarness rewritten as a builder call

        /// <summary>AC3 — the four harness specs' unit order is unchanged</summary>
        [Fact(Skip = Pending)]
        public void ProducesTheSameUnitOrderAsBefore() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAddOrchestrationWithNoOptionalSeams
    {
        // Given: ServiceCollection + AddOrchestration, no IAdSpotVend registered

        /// <summary>AC6 — resolution succeeds without the optional seam</summary>
        [Fact(Skip = Pending)]
        public void ResolvesTheNextItemProvider() => Assert.Fail(Pending);

        /// <summary>AC6 — the Orchestrator's vend is NoOpAdSpotVend</summary>
        [Fact(Skip = Pending)]
        public void BuiltWithTheNoOpVend() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAddOrchestrationWithARegisteredSeam
    {
        // Given: a fake IAdSpotVend registered before AddOrchestration

        /// <summary>AC7 — the registered fake wins over the NoOp</summary>
        [Fact(Skip = Pending)]
        public void BuiltWithTheRegisteredVend() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheRecountAfterTheMove
    {
        // Given: the Orchestration.Tests assembly reflected

        /// <summary>AC9 — no fact was lost in the migration</summary>
        [Fact(Skip = Pending)]
        public void KeepsThePreMoveFactCount() => Assert.Fail(Pending);

        /// <summary>AC9 — no fact was skipped to make the move pass</summary>
        [Fact(Skip = Pending)]
        public void KeepsThePreMoveSkipCount() => Assert.Fail(Pending);
    }
}
