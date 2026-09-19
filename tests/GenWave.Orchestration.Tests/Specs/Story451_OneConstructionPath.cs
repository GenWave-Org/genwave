// STORY-451 — One construction path (gh-#401 · SPEC F184 · PLAN T510–T514)
//
// BDD specification — xUnit. AC1–AC3 drive OrchestratorBuilder (tests/GenWave.TestSupport); AC6/AC7 drive AddOrchestration through a
// ServiceCollection; AC9 is the recount. AC4/AC5/AC8 are pins in Architecture.Tests (Story451_ConstructionPins).
//
// AC1/AC2 went green at T511, AC3/AC9 at T512. AC6/AC7 land at T514, below: they drive
// AddGenWaveOrchestration through a real ServiceCollection rather than OrchestratorBuilder, so the
// optional-seam resolution the extension method itself owns (not the builder's own With* defaults)
// is what each fact actually exercises.

using System.Reflection;
using System.Text.RegularExpressions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureOneConstructionPath
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Every seam AddGenWaveOrchestration itself does not supply (AC6/AC7's own arrange) — the 12
    /// required Orchestrator constructor params it resolves via GetRequiredService, plus
    /// IMediaCatalog/ILogger&lt;MusicSelectionPolicy&gt; so its own TryAddSingleton&lt;MusicSelectionPolicy&gt;
    /// can activate, and (PLAN T522) ILogger&lt;BreakPlanner&gt; — a production host gets this for free
    /// from AddLogging()'s open-generic registration; a bare ServiceCollection like this one needs it
    /// spelled out, now that AddGenWaveOrchestration resolves it via GetRequiredService rather than a
    /// NullLogger fallback. TimeProvider/SpeechDeferralQueue/MusicSelectionPolicy stay unregistered
    /// here on purpose — AddGenWaveOrchestration TryAdds all three itself.
    /// </summary>
    static ServiceCollection RequiredSeamServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IStationIdentityProvider>(new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")));
        services.AddSingleton<IStationScopeProvider>(new FakeStationScopeProvider(new LibraryScope([1L])));
        services.AddSingleton<ICadenceProvider>(new FakeCadenceProvider(new CadenceConfig
        {
            LeadInBeforeEachTrack = false,
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits = 0,
        }));
        services.AddSingleton<IRotationSettingsProvider>(new FakeRotationSettingsProvider(new RotationSettings()));
        services.AddSingleton<ITtsSegmentSource>(new FakeTtsSegmentSource());
        services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        services.AddSingleton<IRenderBudgetProvider>(new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)));
        services.AddSingleton<IBoundaryBiasProvider>(new FakeBoundaryBiasProvider(TimeSpan.Zero));
        services.AddSingleton<ILogger<Orchestrator>>(NullLogger<Orchestrator>.Instance);
        services.AddSingleton<ILogger<MusicSelectionPolicy>>(NullLogger<MusicSelectionPolicy>.Instance);
        services.AddSingleton<ILogger<BreakPlanner>>(NullLogger<BreakPlanner>.Instance);
        services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(TestData.MakeTrackRef("t1")));

        return services;
    }

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

    public sealed class ScenarioAddOrchestrationWithNoOptionalSeams : IAsyncLifetime
    {
        readonly List<MediaItem?> served = [];
        INextItemProvider? orchestrator;

        // Given: ServiceCollection + AddGenWaveOrchestration, no IAdSpotVend registered — the ad
        // cadence still fires every unit, so a real vend WOULD be reachable if one were wired.
        public async Task InitializeAsync()
        {
            var services = RequiredSeamServices();
            services.AddSingleton<IAdCadenceProvider>(new FakeAdCadenceProvider(1));
            services.AddGenWaveOrchestration();

            var provider = services.BuildServiceProvider().GetRequiredService<INextItemProvider>();
            orchestrator = provider;

            var ctx = new PlayoutContext([]);
            served.Add(await provider.GetNextAsync(ctx, CancellationToken.None)); // unit 0 — no trigger
            served.Add(await provider.GetNextAsync(ctx, CancellationToken.None)); // unit 1 — fires
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC6 — resolution succeeds without the optional seam</summary>
        [Fact]
        public void ResolvesTheNextItemProvider() => Assert.NotNull(orchestrator);

        /// <summary>AC6 — the Orchestrator's vend is NoOpAdSpotVend: no ad ever airs</summary>
        [Fact]
        public void BuiltWithTheNoOpVend() => Assert.DoesNotContain(served, item => item?.SegmentKind == SegmentKind.Ad);
    }

    public sealed class ScenarioAddOrchestrationWithARegisteredSeam : IAsyncLifetime
    {
        readonly FakeAdSpotVend vend = new()
        {
            Answer = new MediaItem(
                "spot-1", "/authored/ads/spot-1.wav", "Spot spot-1",
                new Loudness(-14.0, -1.0, true),
                SegmentKind: SegmentKind.Ad),
        };

        // Given: a fake IAdSpotVend registered before AddGenWaveOrchestration — AddGenWaveOrchestration's
        // own TryAddSingleton<IAdSpotVend>(NoOpAdSpotVend.Instance) must lose to it.
        public async Task InitializeAsync()
        {
            var services = RequiredSeamServices();
            services.AddSingleton<IAdCadenceProvider>(new FakeAdCadenceProvider(1));
            services.AddSingleton<IAdSpotVend>(vend);
            services.AddGenWaveOrchestration();

            var orchestrator = services.BuildServiceProvider().GetRequiredService<INextItemProvider>();

            var ctx = new PlayoutContext([]);
            await orchestrator.GetNextAsync(ctx, CancellationToken.None); // unit 0 — no trigger
            await orchestrator.GetNextAsync(ctx, CancellationToken.None); // unit 1 — fires
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC7 — the registered fake wins over the NoOp</summary>
        [Fact]
        public void BuiltWithTheRegisteredVend() => Assert.Equal(1, vend.CallCount);
    }

    public sealed class ScenarioTheRecountAfterTheMove
    {
        // Given: the Orchestration.Tests assembly reflected — every [Fact]/[Theory]-decorated METHOD
        // (not each Theory row) counted once, mirroring how the pre-move baseline was measured.

        const int FactMethodCount = 559; // 557 (556 pre-T512 + 1, PLAN T527 round 1's TheBackAnnounceVoiceMatchesItsSnapshot, ruling 9) + 2 (PLAN T527 round 2, review findings F1/F3: Story456's TheStampedVoiceIsThePlannerResolvedOne + VoiceAndSpeakerAgree — both genuinely new facts, not moved ones); the runner reports 565 cases for these 559 fact methods (two theory methods contributing eight rows, six cases beyond the method count), exactly as the old 562/556 and 563/557 did

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

        /// <summary>AC9 — no fact is skipped outside the STORY-451…459 pendings</summary>
        [Fact]
        public void SkipsNothingOutsideTheEpicPendings()
        {
            var skipsOutsideTheEpic = ReflectFactMethods()
                .Select(x => x.Attribute.Skip)
                .Where(skip => skip is not null && !Regex.IsMatch(skip, "STORY-45[1-9]"));

            Assert.Empty(skipsOutsideTheEpic);
        }
    }
}
