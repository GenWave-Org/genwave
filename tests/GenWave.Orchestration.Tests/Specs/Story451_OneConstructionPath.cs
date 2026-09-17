// STORY-451 — One construction path (gh-#401 · SPEC F184 · PLAN T510–T514)
//
// BDD specification — xUnit. AC1–AC3 drive OrchestratorBuilder (tests/GenWave.TestSupport); AC6/AC7 drive AddOrchestration through a
// ServiceCollection; AC9 is the recount. AC4/AC5/AC8 are pins in Architecture.Tests (Story451_ConstructionPins).
//
// AC1/AC2 went green at T511. The remaining facts are [Fact(Skip = …)] with a loud body — remove the Skip
// only in the task that makes it green (AC3/AC9 → T512, AC6/AC7 → T514).

using System.Reflection;
using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureOneConstructionPath
{
    const string PendingAddOrchestration = "pending: T514 — AddOrchestration resolves optional seams once (STORY-451)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheBuilderWithDefaults
    {
        readonly OrchestratorChain chain;

        // Given: new OrchestratorBuilder().Build()
        public ScenarioTheBuilderWithDefaults()
        {
            chain = new OrchestratorBuilder().Build();
        }

        /// <summary>AC1 — the chain record carries the Orchestrator</summary>
        [Fact]
        public void ExposesTheOrchestrator() => Assert.NotNull(chain.Orchestrator);

        /// <summary>AC1 — the deferral queue is the one the Orchestrator was built with</summary>
        [Fact]
        public void ExposesTheDeferralQueue() => Assert.NotNull(chain.Queue);

        /// <summary>AC1 — a FakeTimeProvider drives the chain by default</summary>
        [Fact]
        public void ExposesTheFakeClock() => Assert.IsType<FakeTimeProvider>(chain.Time);

        /// <summary>AC1 — the tts fake is reachable for assertions by default</summary>
        [Fact]
        public void ExposesTheTtsFake() => Assert.IsType<FakeTtsSegmentSource>(chain.Tts);

        /// <summary>AC1 — the capturing event sink is reachable by default</summary>
        [Fact]
        public void ExposesTheEventSink() => Assert.IsType<CapturingStationEventSink>(chain.Events);

        /// <summary>AC1 — the fake catalog is reachable by default</summary>
        [Fact]
        public void ExposesTheCatalog() => Assert.IsType<FakeMediaCatalog>(chain.Catalog);

        /// <summary>
        /// AC1 — the estimator slot is reachable. Its default is deliberately null, not a fake — see
        /// <see cref="OrchestratorBuilder"/>'s own remarks: the one estimator double this project owns
        /// (<see cref="CapturingPatterDurationEstimator"/>) is not behaviorally equivalent to
        /// Orchestrator's own internal fallback, so defaulting to it here would silently change
        /// boundary-fit behavior for every spec that never overrides this seam.
        /// </summary>
        [Fact]
        public void ExposesThePatterEstimator() => Assert.Null(chain.PatterEstimator);
    }

    public sealed class ScenarioAnOverriddenAdSpotVend : IAsyncLifetime
    {
        readonly FakeAdSpotVend vend = new()
        {
            Answer = new MediaItem(
                "spot-1", "/authored/ads/spot-1.wav", "Spot spot-1",
                new Loudness(-14.0, -1.0, true),
                SegmentKind: SegmentKind.Ad),
        };

        // Given: builder.WithAdSpotVend(fake) and ad cadence every unit; two units served
        public async Task InitializeAsync()
        {
            var chain = new OrchestratorBuilder()
                .WithAdCadence(new FakeAdCadenceProvider(1))
                .WithAdSpotVend(vend)
                .Build();

            var ctx = new PlayoutContext([]);
            await chain.Orchestrator.GetNextAsync(ctx, CancellationToken.None); // unit 0 — no trigger
            await chain.Orchestrator.GetNextAsync(ctx, CancellationToken.None); // unit 1 — fires
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC2 — the seam override reaches the Orchestrator</summary>
        [Fact]
        public void TheFakeReceivedOneVend() => Assert.Equal(1, vend.CallCount);
    }

    public sealed class ScenarioTheHarnessRidesTheBuilder
    {
        // Given: ProductionChainHarness rewritten as a builder call — reruns the exact two-DJ
        // noon-boundary shape Story243_DjsHandOffAudibly.cs/Story307_CeremonyNamesTheShow.cs each
        // exercise through ProductionChainHarness.BuildProductionChain directly (rather than their own
        // inline copy), pinning the resulting unit sequence so a future change to the builder's
        // wiring order trips this fact first.

        static readonly DayOfWeek Monday = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero).DayOfWeek;
        static readonly DateTimeOffset JustBeforeNoon = new(2026, 3, 2, 11, 55, 0, TimeSpan.Zero);

        static ScheduleWeekSnapshot TwoDjSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        static FakePersonaStore TwoDjStore()
        {
            var store = new FakePersonaStore();
            store.Add(TestData.MakePersona(10, "DJ Alpha", "af_alpha"));
            store.Add(TestData.MakePersona(20, "DJ Beta", "af_beta"));
            return store;
        }

        static bool IsSignOff(MediaItem item) =>
            item.MediaId.StartsWith("tts:signoff", StringComparison.OrdinalIgnoreCase);

        static bool IsSignOn(MediaItem item) =>
            item.MediaId.StartsWith("tts:signon", StringComparison.OrdinalIgnoreCase);

        static string Classify(MediaItem item) =>
            IsSignOff(item) ? "SignOff" : IsSignOn(item) ? "SignOn" : "Music";

        /// <summary>AC3 — the four harness specs' unit order is unchanged</summary>
        [Fact]
        public async Task ProducesTheSameUnitOrderAsBefore()
        {
            var chain = ProductionChainHarness.BuildProductionChain(
                TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            var kinds = new List<string>();
            for (var i = 0; i < 12; i++)
            {
                var item = await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None)
                    ?? throw new InvalidOperationException("Expected GetNextAsync to produce a media item.");
                kinds.Add(Classify(item));
                chain.Time.Advance(TimeSpan.FromSeconds(30));
            }

            Assert.Equal(
                [
                    "Music", "Music", "Music", "Music", "Music", "Music",
                    "Music", "SignOff", "SignOn", "Music", "Music", "Music",
                ],
                kinds);
        }
    }

    public sealed class ScenarioAddOrchestrationWithNoOptionalSeams
    {
        // Given: ServiceCollection + AddOrchestration, no IAdSpotVend registered

        /// <summary>AC6 — resolution succeeds without the optional seam</summary>
        [Fact(Skip = PendingAddOrchestration)]
        public void ResolvesTheNextItemProvider() => Assert.Fail(PendingAddOrchestration);

        /// <summary>AC6 — the Orchestrator's vend is NoOpAdSpotVend</summary>
        [Fact(Skip = PendingAddOrchestration)]
        public void BuiltWithTheNoOpVend() => Assert.Fail(PendingAddOrchestration);
    }

    public sealed class ScenarioAddOrchestrationWithARegisteredSeam
    {
        // Given: a fake IAdSpotVend registered before AddOrchestration

        /// <summary>AC7 — the registered fake wins over the NoOp</summary>
        [Fact(Skip = PendingAddOrchestration)]
        public void BuiltWithTheRegisteredVend() => Assert.Fail(PendingAddOrchestration);
    }

    public sealed class ScenarioTheRecountAfterTheMove
    {
        // Given: the Orchestration.Tests assembly reflected — every [Fact]/[Theory]-decorated METHOD
        // (not each Theory row) counted once, mirroring how the pre-move baseline was measured.

        const int FactMethodCount = 556; // reflected [Fact]/[Theory] methods, measured before T512 moved any site (the move adds and removes no attribute, so pre = post); the runner reports 562 cases = 556 + theory rows
        const int SkipCount = 108; // 111 pre-move minus the 3 AC3/AC9 facts this task un-skips

        static IReadOnlyList<(MethodInfo Method, FactAttribute Attribute)> ReflectFactMethods() =>
            typeof(FeatureOneConstructionPath).Assembly
                .GetTypes()
                .SelectMany(t => t.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .SelectMany(m => m.GetCustomAttributes().OfType<FactAttribute>()
                    .Select(a => (Method: m, Attribute: a)))
                .ToList();

        /// <summary>AC9 — no fact was lost in the migration</summary>
        [Fact]
        public void KeepsThePreMoveFactCount() =>
            Assert.Equal(FactMethodCount, ReflectFactMethods().Count);

        /// <summary>AC9 — no fact was skipped to make the move pass</summary>
        [Fact]
        public void KeepsThePreMoveSkipCount()
        {
            var skipped = ReflectFactMethods().Where(x => x.Attribute.Skip is not null).ToList();

            Assert.Equal(SkipCount, skipped.Count);
            Assert.All(skipped, x => Assert.Matches("STORY-45[1-9]", x.Attribute.Skip));
        }
    }
}
