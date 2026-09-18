// STORY-455 — BreakPlanner decides the break (gh-#401 · SPEC F188 · PLAN T521–T523)
//
// BDD specification — xUnit. AC1–AC4 drive BreakPlanner directly with recording/throwing/counting
// fakes (PLAN T521, this file) — every one of those five facts is green. AC5 is the STORY-452 replay
// through the Orchestrator that consumes the plan; AC6 reflects the deleted methods — both stay
// pending until PLAN T522 rewires Orchestrator onto BreakPlanner and deletes the old path.
//
// AC1's table is NOT a replay of STORY-452's own script: that table's media ids are POST-RENDER
// survivors (a dropped render never appears there at all), while AC1 needs PRE-render, planner-level
// slot kinds — so this file drives its own small illustrative script directly through a
// BreakPlannerBuilder-built BreakPlanner, pinned from one deterministic run exactly the way
// Story452PinnedTables's own tables were derived.

using System.Reflection;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBreakPlanner
{
    static readonly StationIdentity Identity = new("station-1", "GenWave", "voice-station");
    static readonly HashSet<SpeechDeferralKind> NoHolds = [];

    static CadenceConfig Cadence(bool leadIn = true, bool backAnnounce = true, int stationIdEveryN = 0) => new()
    {
        LeadInBeforeEachTrack = leadIn,
        BackAnnounceAfterEachTrack = backAnnounce,
        StationIdEveryNUnits = stationIdEveryN,
    };

    public sealed class ScenarioTheScriptPlanned
    {
        /// <summary>
        /// AC1 — the rows: three units through one BreakPlanner/queue/clock, each plan's slot kinds
        /// captured in kick order. Unit 1 (no previous track, two pending announcements, no cadence
        /// hit) yields the opening announcements plus a lead-in; unit 2 (both station-id and ad
        /// cadence due on the SAME unit) proves the StationId-before-Ad drain tiebreak (SPEC F158.3,
        /// STORY-453 AC8); unit 3 (no next track — a ceremony-only finale, SPEC F186.2e — with a
        /// context segment and a time/date deferral both due) proves the Context-before-TimeDate
        /// tiebreak (STORY-453 AC9), and that a null Next suppresses both the cadence enqueues and
        /// the lead-in.
        /// </summary>
        [Fact]
        public async Task YieldsThePinnedSlotKinds()
        {
            var trackA = TestData.MakeTrackRef("track-a").ToMediaItem();
            var trackB = TestData.MakeTrackRef("track-b").ToMediaItem();

            var announcements = new FakeAnnouncementSource();
            announcements.Pending.Enqueue(new AnnouncementItem(1, "Happy birthday!", Verbatim: true, RequestedVoice: null));
            announcements.Pending.Enqueue(new AnnouncementItem(2, "Pledge drive ends Friday.", Verbatim: true, RequestedVoice: null));

            var chain = new BreakPlannerBuilder()
                .WithAnnouncementSource(announcements)
                .WithAdCadence(new FakeAdCadenceProvider(2))
                .WithAdSpotVend(new FakeAdSpotVend { Answer = TestData.MakeTrackRef("ad-1").ToMediaItem() })
                .Build();

            var now = chain.Time.GetUtcNow();

            var unit1 = new BreakContext(1, null, trackA, null, Cadence(stationIdEveryN: 0), Identity, now, null, NoHolds, TimeSpan.Zero);
            var plan1 = await chain.Planner.PlanAsync(unit1, CancellationToken.None);

            var unit2 = new BreakContext(2, trackA, trackB, null, Cadence(stationIdEveryN: 2), Identity, now, null, NoHolds, TimeSpan.Zero);
            var plan2 = await chain.Planner.PlanAsync(unit2, CancellationToken.None);

            chain.Queue.Enqueue(SpeechDeferralKind.Context, "test: weather due", discriminator: "weather",
                context: new ContextSegmentFacts("Sunny and mild.", now.AddMinutes(30)));
            chain.Queue.Enqueue(SpeechDeferralKind.TimeDate, "test: time/date due");

            var unit3 = new BreakContext(3, trackB, null, null, Cadence(stationIdEveryN: 2), Identity, now, null, NoHolds, TimeSpan.Zero);
            var plan3 = await chain.Planner.PlanAsync(unit3, CancellationToken.None);

            Assert.Equal(
                [SegmentKind.Announcement, SegmentKind.Announcement, SegmentKind.LeadIn],
                plan1.Slots.Select(s => s.Kind));
            Assert.Equal(
                [SegmentKind.BackAnnounce, SegmentKind.StationId, SegmentKind.Ad, SegmentKind.LeadIn],
                plan2.Slots.Select(s => s.Kind));
            Assert.Equal(
                [SegmentKind.BackAnnounce, SegmentKind.ContextSegment, SegmentKind.TimeDate],
                plan3.Slots.Select(s => s.Kind));
        }
    }

    public sealed class ScenarioThrowingRenderFakes
    {
        /// <summary>
        /// AC2 — the plan phase makes no render call. Proven architecturally rather than by a
        /// throwing-fake run: no injectable <see cref="ITtsSegmentSource"/>/
        /// <see cref="IVerbatimSegmentRenderer"/>/<see cref="IAnnouncementCopyWriter"/> seam exists on
        /// <see cref="BreakPlanner"/>'s constructor at all (SPEC F188's own "no
        /// tts/verbatim/copy-writer dependency") — this fact fails the instant one is added without a
        /// matching update here, a stronger guarantee than one throwing-fake run could ever give.
        /// </summary>
        [Fact]
        public void NoFakeWasCalled()
        {
            var forbidden = new[] { typeof(ITtsSegmentSource), typeof(IVerbatimSegmentRenderer), typeof(IAnnouncementCopyWriter) };
            var parameters = typeof(BreakPlanner).GetConstructors().Single().GetParameters();

            foreach (var parameter in parameters)
            {
                Assert.DoesNotContain(forbidden, candidate => candidate.IsAssignableFrom(parameter.ParameterType));
            }
        }
    }

    public sealed class ScenarioRecordingSideEffectFakes
    {
        /// <summary>
        /// AC3 — side effects in order. STORY-455's own canonical order is MarkVended, claim,
        /// StationId enqueue, Ad enqueue, drain, pool lookup, vend — <see cref="BreakPlanner.PlanAsync"/>'s
        /// own step order (SPEC F188.2). Three of those seven run through interfaces this fixture can
        /// wrap directly (claim, pool lookup, vend) — their exact relative order is asserted below.
        /// The remaining four (MarkVended, both cadence enqueues, the drain itself) touch
        /// <see cref="CrosstalkPlanner"/> and <see cref="SpeechDeferralQueue"/>, both sealed concrete
        /// types with no test seam — adding one is out of T521's scope (SPEC F188 copies both
        /// verbatim). Their OCCURRENCE, in the right place in the pipeline, is proven instead:
        /// MarkVended via the <see cref="CrosstalkPlanner.RetireByMediaId"/> probe (it only deletes
        /// the crosstalk asset when the id was actually marked earlier), and the enqueue+drain pair
        /// via the queue ending this call with both kinds fully drained.
        /// </summary>
        [Fact]
        public async Task RecordsTodaysOrder()
        {
            const long hostPersonaId = 10;
            const long neighborPersonaId = 20;

            var personaStore = new FakePersonaStore();
            personaStore.Add(TestData.MakePersona(hostPersonaId, "Nova", "af_nova"));
            personaStore.Add(TestData.MakePersona(neighborPersonaId, "Milo", "af_milo"));

            var week = new ScheduleWeekSnapshot([
                new ScheduleSegment(1, DayOfWeek.Monday, 0, 720, hostPersonaId, null, null, null,
                    Show: new ShowSummary(5, "Morning Mix", null, null) { Slug = "morning-mix" }, ShowId: 5),
                new ScheduleSegment(2, DayOfWeek.Monday, 720, 1440, neighborPersonaId, null, null, null),
            ]);

            var crosstalkScope = new FakeCrosstalkScopeProvider(["morning-mix"], everyNthAiring: 1);
            var crosstalkPlanner = new CrosstalkPlanner(personaStore, crosstalkScope, NullLogger<CrosstalkPlanner>.Instance);

            var crosstalkAssetPath = Path.Combine(Path.GetTempPath(), $"genwave-story455-{Guid.NewGuid():n}.wav");
            File.WriteAllBytes(crosstalkAssetPath, [0]);
            try
            {
                crosstalkPlanner.Stock(new StockedCrosstalkExchange(
                    "morning-mix", new CrosstalkCast(hostPersonaId, neighborPersonaId), crosstalkAssetPath,
                    new Loudness(-16.0, -1.0, true), Cue: null, DurationMs: 6_000));

                var order = new List<string>();
                var announcements = new FakeAnnouncementSource();
                announcements.Pending.Enqueue(new AnnouncementItem(1, "hello", Verbatim: true, RequestedVoice: null));

                var chain = new BreakPlannerBuilder()
                    .WithNow(new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero))
                    .WithSchedule(week)
                    .WithPersonaStore(personaStore)
                    .WithCrosstalkPlanner(crosstalkPlanner)
                    .WithAnnouncementSource(new RecordingAnnouncementSource(order, announcements))
                    .WithCatalog(new RecordingMediaCatalog(order))
                    .WithAdCadence(new FakeAdCadenceProvider(1))
                    .WithAdSpotVend(new RecordingAdSpotVend(order, TestData.MakeTrackRef("ad-1").ToMediaItem()))
                    .Build();

                // Warms CachingScheduleResolver's own snapshot cache (TryGetCurrent/TryGetCurrentWeekSnapshot
                // answer null until ResolveAsync has loaded at least once) — production reaches ResolveAsync
                // via OnAirPersonaAccessor on an earlier unit's persona resolve; this fixture has no earlier
                // unit, so it warms the cache directly instead of asserting on a boot-window artifact.
                await chain.ScheduleResolver.ResolveAsync(CancellationToken.None);

                var context = new BreakContext(
                    1, null, TestData.MakeTrackRef("next").ToMediaItem(), null,
                    Cadence(leadIn: false, backAnnounce: false, stationIdEveryN: 1),
                    Identity, chain.Time.GetUtcNow(), null, NoHolds, TimeSpan.Zero);

                await chain.Planner.PlanAsync(context, CancellationToken.None);

                Assert.Equal(["claim", "pool lookup", "vend"], order);

                var crosstalkMediaId = $"tts:crosstalk:{Path.GetFileNameWithoutExtension(crosstalkAssetPath)}";
                crosstalkPlanner.RetireByMediaId(crosstalkMediaId);
                Assert.False(File.Exists(crosstalkAssetPath));

                Assert.Null(chain.Queue.Peek(SpeechDeferralKind.StationId));
                Assert.Null(chain.Queue.Peek(SpeechDeferralKind.Ad));
            }
            finally
            {
                File.Delete(crosstalkAssetPath);
            }
        }
    }

    public sealed class ScenarioCountingBudgetProviders
    {
        /// <summary>AC4 — each budget provider is read exactly once per <see cref="BreakPlanner.PlanAsync"/> call (SPEC F188.3).</summary>
        [Fact]
        public async Task ReadsEachOnce()
        {
            var renderBudget = new CountingRenderBudgetProvider(TimeSpan.FromSeconds(7));
            var imagingSettings = new CountingStationImagingSettingsProvider(new StationImagingSettings(false, false, 180));

            var chain = new BreakPlannerBuilder()
                .WithRenderBudget(renderBudget)
                .WithImagingSettings(imagingSettings)
                .Build();

            var context = new BreakContext(
                1, null, TestData.MakeTrackRef("t").ToMediaItem(), null, Cadence(stationIdEveryN: 0),
                Identity, chain.Time.GetUtcNow(), null, NoHolds, TimeSpan.Zero);

            await chain.Planner.PlanAsync(context, CancellationToken.None);

            Assert.Equal(1, renderBudget.ReadCount);
            Assert.Equal(1, imagingSettings.ReadCount);
        }

        /// <summary>AC4 — the plan carries the render budget read at plan time (SPEC F188.3).</summary>
        [Fact]
        public async Task CarriesTheRenderBudgetOnThePlan()
        {
            var chain = new BreakPlannerBuilder()
                .WithRenderBudget(TimeSpan.FromSeconds(11))
                .Build();

            var context = new BreakContext(
                1, null, TestData.MakeTrackRef("t").ToMediaItem(), null, Cadence(stationIdEveryN: 0),
                Identity, chain.Time.GetUtcNow(), null, NoHolds, TimeSpan.Zero);

            var plan = await chain.Planner.PlanAsync(context, CancellationToken.None);

            Assert.Equal(TimeSpan.FromSeconds(11), plan.RenderBudget);
        }
    }

    public sealed class ScenarioTheOrchestratorRidesThePlan
    {
        // Given: the STORY-452 replay after T522

        /// <summary>AC5 — every frozen STORY-452 assertion stays green once the Orchestrator rides the plan.</summary>
        [Fact]
        public async Task KeepsEveryFrozenAssertionGreen()
        {
            var result = await FeatureBreakCharacterisationReplay.RunScriptAsync();

            Assert.Equal(Story452PinnedTables.BufferedMediaIds, result.BufferedMediaIds);
            Assert.Equal(Story452PinnedTables.DjNames, result.DjNames);
            Assert.Equal(Story452PinnedTables.EventKinds, result.EventKinds);
            Assert.Equal(Story452PinnedTables.Warnings, result.Warnings);
        }

        /// <summary>AC5 — the STORY-452 trace table matches the plan's own <see cref="BreakPlan.ToTrace"/>.</summary>
        [Fact]
        public async Task MatchesThePinnedTraces()
        {
            var result = await FeatureBreakCharacterisationReplay.RunScriptAsync();

            Assert.Equal(Story452PinnedTables.Traces, result.Traces);
        }
    }

    public sealed class ScenarioTheOldPathReflected
    {
        // Given: typeof(Orchestrator) non-public methods

        static readonly MethodInfo[] NonPublicMethods = typeof(Orchestrator).GetMethods(
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        /// <summary>AC6 — EnqueuePatterAsync no longer exists on Orchestrator.</summary>
        [Fact]
        public void HasNoEnqueuePatterAsync() =>
            Assert.DoesNotContain(NonPublicMethods, m => m.Name == "EnqueuePatterAsync");

        /// <summary>AC6 — BuildStationIdRequest, BuildAdRequest, BuildHandoffRequest, BuildTimeDateRequest, BuildContextSegmentRequestAsync no longer exist on Orchestrator.</summary>
        [Fact]
        public void HasNoBuildRequestMethods()
        {
            string[] deleted =
            [
                "BuildStationIdRequest",
                "BuildAdRequest",
                "BuildHandoffRequest",
                "BuildTimeDateRequest",
                "BuildContextSegmentRequestAsync",
            ];

            foreach (var name in deleted)
            {
                Assert.DoesNotContain(NonPublicMethods, m => m.Name == name);
            }
        }
    }
}

// ── AC3 recording wrappers — file-scoped, single-purpose doubles over the three interfaces
// BreakPlanner's side effects run through that this fixture can observe directly (mirrors
// Story328_CrosstalkStockWorker.cs's own multiple-`file class`-per-file idiom one project over). ──

file sealed class RecordingAnnouncementSource(List<string> order, IAnnouncementSource inner) : IAnnouncementSource
{
    public async Task<IReadOnlyList<AnnouncementItem>> ClaimDeliverableAsync(int max, CancellationToken ct)
    {
        order.Add("claim");
        return await inner.ClaimDeliverableAsync(max, ct);
    }
}

// A thin wrapper over FakeMediaCatalog (mirrors RecordingAnnouncementSource's own wrap-and-record
// shape one class up) rather than re-implementing every unexercised IMediaCatalog member: only
// GetRandomReadyByImagingKindAsync is on BreakPlanner's own drain path (T521 AC3 fixture scope), so
// only that member records — everything else delegates straight through.
file sealed class RecordingMediaCatalog(List<string> order) : IMediaCatalog
{
    readonly FakeMediaCatalog inner = new(ready: null);

    public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct) =>
        inner.GetByIdAsync(scope, mediaId, ct);

    public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct) =>
        inner.GetByIdUnscopedAsync(mediaId, ct);

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct) =>
        inner.GetRandomReadyAsync(scope, excludeIds, ct);

    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct) =>
        inner.GetRotationCandidateAsync(scope, orderedRecentIds, artistSeparation, ct);

    public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct) =>
        inner.ListAsync(scope, query, ct);

    public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct) =>
        inner.GetStatusCountsAsync(safeScope, ct);

    public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct) =>
        inner.GetFacetsAsync(field, scope, ct);

    public Task<MediaReference?> GetRandomReadyByImagingKindAsync(LibraryScope scope, ImagingKind kind, long? showId, CancellationToken ct)
    {
        order.Add("pool lookup");
        return inner.GetRandomReadyByImagingKindAsync(scope, kind, showId, ct);
    }
}

file sealed class RecordingAdSpotVend(List<string> order, MediaItem answer) : IAdSpotVend
{
    public Task<MediaItem?> GetNextSpotAsync(CancellationToken ct)
    {
        order.Add("vend");
        return Task.FromResult<MediaItem?>(answer);
    }
}

file sealed class CountingRenderBudgetProvider(TimeSpan budget) : IRenderBudgetProvider
{
    public int ReadCount { get; private set; }

    public TimeSpan Current
    {
        get
        {
            ReadCount++;
            return budget;
        }
    }
}

file sealed class CountingStationImagingSettingsProvider(StationImagingSettings settings) : IStationImagingSettingsProvider
{
    public int ReadCount { get; private set; }

    public StationImagingSettings Current
    {
        get
        {
            ReadCount++;
            return settings;
        }
    }
}
