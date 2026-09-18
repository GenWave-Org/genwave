// STORY-453 — The break contract has one fact per cell (gh-#401 · SPEC F186 · PLAN T517–T519)
//
// BDD specification — xUnit. One fact per cell of the SPEC F186 table, all against the unsplit code. Rows AC1–AC17 (T517) and
// drops AC18–AC23 (T518) are GREEN. AC24 (the two follow-up issues) is a manual check on PR-2's body. F186.4: back-announce on a
// ceremony-only unit STAYS (Dean, 2026-09-17); the other two cells are scripted as-built and filed as follow-ups.
//
// Every AC1–AC17 scenario is arranged through OrchestratorBuilder/OrchestratorChain exactly as
// Story452_BreakCharacterisationReplay.cs is (that file stays FROZEN — nothing here copies its pinned
// tables, each fact below arranges only what its own Given names). Deterministic: FakeTimeProvider
// only, no Guid/temp-random names in media ids — the one exception is the crosstalk asset, which
// Story452's own idiom already established: a FIXED, unique-per-fact temp path, not
// Path.GetTempFileName().

using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Orchestration.Tests.Specs;

// This class spans three files (~300-line budget, csharp-best-practices): AC1–AC9 (this file),
// AC10–AC17 + AC24 (Story453_BreakContractSlots.cs) and AC18–AC23 (Story453_BreakContractDrops.cs)
// share the same helpers declared below.
public static partial class FeatureBreakContract
{
    const long HostPersonaId = 10;
    const long NeighborPersonaId = 20;
    const string ShowSlug = "morning-mix";
    static readonly HandoffContext Handoff = new("default", "Outgoing", "Incoming");

    static async Task<MediaItem> PullAsync(Orchestrator orchestrator, PlayoutContext? ctx = null)
    {
        var item = await orchestrator.GetNextAsync(ctx ?? new PlayoutContext([]), CancellationToken.None);
        Assert.NotNull(item);
        return item;
    }

    static async Task<List<MediaItem>> PullManyAsync(Orchestrator orchestrator, int count, PlayoutContext? ctx = null)
    {
        var items = new List<MediaItem>();
        for (var i = 0; i < count; i++) items.Add(await PullAsync(orchestrator, ctx));
        return items;
    }

    /// <summary>The one place a scenario's three cadence knobs become a <see cref="CadenceConfig"/>.</summary>
    static CadenceConfig Cadence(bool leadIn, bool backAnnounce, int stationIdEvery) => new()
    {
        LeadInBeforeEachTrack = leadIn,
        BackAnnounceAfterEachTrack = backAnnounce,
        StationIdEveryNUnits = stationIdEvery,
    };

    /// <summary>Wires a real schedule/persona-store/crosstalk-planner chain (SPEC F111/F117) so
    /// TryVendCrosstalkForThisBreak can resolve an on-air show slug — the shared arrange for AC3/AC4/AC15.
    /// <paramref name="tts"/> is null for AC3/AC4; AC15 passes its own fake to assert on the render seam.</summary>
    static (OrchestratorChain Chain, CrosstalkPlanner Planner, FakeMediaCatalog Catalog) BuildCrosstalkChain(
        CadenceConfig cadence, FakeTtsSegmentSource? tts = null)
    {
        var personaStore = new FakePersonaStore();
        personaStore.Add(TestData.MakePersona(HostPersonaId, "Host", "af_host"));
        personaStore.Add(TestData.MakePersona(NeighborPersonaId, "Neighbor", "af_neighbor"));

        // Two segments (not one): CrosstalkPlanner.TryCastPersonas derives a cast from grid adjacency —
        // a single all-day segment leaves the host with no distinct neighbor, so any stocked exchange
        // reads as stale before it can ever vend. Mirrors Story452_BreakCharacterisationReplay's own
        // precedent exactly (same two boundaries, same neighbor persona, same clock below).
        var week = new ScheduleWeekSnapshot([
            new ScheduleSegment(1, DayOfWeek.Monday, 0, 720, HostPersonaId, null, null, null,
                Show: new ShowSummary(5, "Morning Mix", null, null) { Slug = ShowSlug }, ShowId: 5),
            new ScheduleSegment(2, DayOfWeek.Monday, 720, 1440, NeighborPersonaId, null, null, null),
        ]);

        var crosstalkScope = new FakeCrosstalkScopeProvider([ShowSlug], everyNthAiring: 1);
        var planner = new CrosstalkPlanner(personaStore, crosstalkScope, NullLogger<CrosstalkPlanner>.Instance);
        var catalog = new FakeMediaCatalog(TestData.MakeTrackRef("t1"));

        var builder = new OrchestratorBuilder()
            .WithTime(new FakeTimeProvider(new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero)))
            .WithSchedule(week)
            .WithPersonaStore(personaStore)
            .WithCadence(cadence)
            .WithCrosstalkPlanner(planner)
            .WithCatalog(catalog)
            .WithLookahead(TimeSpan.FromMinutes(10));
        if (tts is not null) builder.WithTts(tts);

        return (builder.Build(), planner, catalog);
    }

    static void StockReadyExchange(CrosstalkPlanner planner, string assetPath)
    {
        File.WriteAllBytes(assetPath, [0]);
        planner.Stock(new StockedCrosstalkExchange(
            ShowSlug, new CrosstalkCast(HostPersonaId, NeighborPersonaId), assetPath, new Loudness(-16.0, -1.0, true),
            Cue: null, DurationMs: 6_000));
    }

    // HAPPY PATH
    public sealed class ScenarioBackAnnounceWithAPreviousTrack
    {
        // Given: cadence back-announce on, a previous track
        /// <summary>AC1 — the first spoken item names the previous track</summary>
        [Fact]
        public async Task BuffersABackAnnounceFirst()
        {
            var cadence = Cadence(leadIn: false, backAnnounce: true, stationIdEvery: 0);
            var tts = new FakeTtsSegmentSource();
            var catalog = FakeMediaCatalog.WithPool([TestData.MakeTrackRef("t1"), TestData.MakeTrackRef("t2")]);
            var chain = new OrchestratorBuilder().WithCadence(cadence).WithTts(tts).WithCatalog(catalog).Build();

            var previous = await PullAsync(chain.Orchestrator);
            var items = await PullManyAsync(chain.Orchestrator, 2);

            Assert.Equal(SegmentKind.BackAnnounce, items[0].SegmentKind);
            var backAnnounce = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.BackAnnounce);
            var track = backAnnounce.Track;
            Assert.NotNull(track);
            Assert.Equal(previous.MediaId, track.MediaId);
        }
    }

    public sealed class ScenarioTheFirstUnit
    {
        // Given: no previous track
        /// <summary>AC2 — nothing to announce</summary>
        [Fact]
        public async Task BuffersNoBackAnnounce()
        {
            var cadence = Cadence(leadIn: false, backAnnounce: true, stationIdEvery: 0);
            var tts = new FakeTtsSegmentSource();
            var chain = new OrchestratorBuilder().WithCadence(cadence).WithTts(tts).Build();

            await PullAsync(chain.Orchestrator);

            Assert.DoesNotContain(tts.Requests, r => r.Kind == SegmentKind.BackAnnounce);
        }
    }

    public sealed class ScenarioAVendableCrosstalk
    {
        // Given: a crosstalk vend, a next track, no drainAsOf, no ceremony due
        /// <summary>AC3 — the crosstalk file sits after the back-announce</summary>
        [Fact]
        public async Task FollowsTheBackAnnounce()
        {
            var cadence = Cadence(leadIn: false, backAnnounce: true, stationIdEvery: 0);
            var assetPath = Path.Combine(Path.GetTempPath(), "genwave-story453-ac3-crosstalk.wav");
            var (chain, planner, _) = BuildCrosstalkChain(cadence);

            await PullAsync(chain.Orchestrator); // primes the previous track

            StockReadyExchange(planner, assetPath);
            var items = await PullManyAsync(chain.Orchestrator, 3);

            Assert.Equal(SegmentKind.BackAnnounce, items[0].SegmentKind);
            Assert.Equal(SegmentKind.Crosstalk, items[1].SegmentKind);
            Assert.Null(items[2].SegmentKind);
        }
    }

    public sealed class ScenarioACrosstalkAndADueSignOff
    {
        // Given: a vendable crosstalk with a SignOff due now
        /// <summary>AC4 — a drained ceremony excludes crosstalk</summary>
        [Fact]
        public async Task BuffersNoCrosstalk()
        {
            var cadence = Cadence(leadIn: false, backAnnounce: false, stationIdEvery: 0);
            var assetPath = Path.Combine(Path.GetTempPath(), "genwave-story453-ac4-crosstalk.wav");
            var (chain, planner, _) = BuildCrosstalkChain(cadence);
            StockReadyExchange(planner, assetPath);

            chain.Queue.Enqueue(SpeechDeferralKind.SignOff, "test: due signoff", chain.Time.GetUtcNow(), Handoff);

            var items = await PullManyAsync(chain.Orchestrator, 2);

            Assert.DoesNotContain(items, i => i.SegmentKind == SegmentKind.Crosstalk);
        }
    }

    public sealed class ScenarioThreePendingAnnouncements
    {
        // Given: three claimable announcements
        /// <summary>AC5 — the cap is two per break</summary>
        [Fact]
        public async Task BuffersExactlyTwo()
        {
            var source = new FakeAnnouncementSource();
            source.Pending.Enqueue(new AnnouncementItem(1, "One", true, null));
            source.Pending.Enqueue(new AnnouncementItem(2, "Two", true, null));
            source.Pending.Enqueue(new AnnouncementItem(3, "Three", true, null));
            var renderer = new FakeVerbatimSegmentRenderer();
            var chain = new OrchestratorBuilder().WithAnnouncementSource(source).WithAnnouncementRenderer(renderer).Build();

            var items = await PullManyAsync(chain.Orchestrator, 3);

            Assert.Equal(2, items.Count(i => i.SegmentKind == SegmentKind.Announcement));
        }
    }

    public sealed class ScenarioStationIdCadenceHit
    {
        // Given: StationIdEveryNUnits = 2, unit 2
        /// <summary>AC6 — the cadence enqueues a StationId</summary>
        [Fact]
        public async Task BuffersAStationIdAfterTheAnnouncements()
        {
            var cadence = Cadence(leadIn: false, backAnnounce: false, stationIdEvery: 2);
            var source = new FakeAnnouncementSource();
            var renderer = new FakeVerbatimSegmentRenderer();
            var chain = new OrchestratorBuilder().WithCadence(cadence).WithAnnouncementSource(source).WithAnnouncementRenderer(renderer).Build();

            await PullManyAsync(chain.Orchestrator, 2); // units 0-1 — the cadence has not hit yet

            source.Pending.Enqueue(new AnnouncementItem(1, "One", true, null));
            var items = await PullManyAsync(chain.Orchestrator, 3); // unit 2 — cadence hits

            Assert.Equal(SegmentKind.Announcement, items[0].SegmentKind);
            Assert.Equal(SegmentKind.StationId, items[1].SegmentKind);
            Assert.Null(items[2].SegmentKind);
        }
    }

    public sealed class ScenarioAdCadenceHit
    {
        // Given: ad cadence every 2 units and a vendable spot, unit 2
        /// <summary>AC7 — the vended spot is buffered</summary>
        [Fact]
        public async Task BuffersTheSpot()
        {
            var adCadence = new FakeAdCadenceProvider(2);
            var spot = new MediaItem("ad-spot-1", "/ads/spot1.mp3", "Ad Spot", new Loudness(-16.0, -1.0, true));
            var adSpotVend = new FakeAdSpotVend { Answer = spot };
            var chain = new OrchestratorBuilder().WithAdCadence(adCadence).WithAdSpotVend(adSpotVend).Build();

            await PullManyAsync(chain.Orchestrator, 2); // units 0-1 — the cadence has not hit yet
            var items = await PullManyAsync(chain.Orchestrator, 2); // unit 2 — cadence hits

            Assert.Equal(SegmentKind.Ad, items[0].SegmentKind);
            Assert.Equal("ad-spot-1", items[0].MediaId);
            Assert.Null(items[1].SegmentKind);
        }
    }

    public sealed class ScenarioStationIdAndAdDueTogether
    {
        // Given: both cadences hit the same unit
        /// <summary>AC8 — the drain tiebreak</summary>
        [Fact]
        public async Task StationIdPrecedesTheAd()
        {
            var cadence = Cadence(leadIn: false, backAnnounce: false, stationIdEvery: 2);
            var adCadence = new FakeAdCadenceProvider(2);
            var spot = new MediaItem("ad-spot-1", "/ads/spot1.mp3", "Ad Spot", new Loudness(-16.0, -1.0, true));
            var adSpotVend = new FakeAdSpotVend { Answer = spot };
            var chain = new OrchestratorBuilder().WithCadence(cadence).WithAdCadence(adCadence).WithAdSpotVend(adSpotVend).Build();

            await PullManyAsync(chain.Orchestrator, 2); // units 0-1 — neither cadence has hit yet
            var items = await PullManyAsync(chain.Orchestrator, 3); // unit 2 — both hit together

            Assert.Equal(SegmentKind.StationId, items[0].SegmentKind);
            Assert.Equal(SegmentKind.Ad, items[1].SegmentKind);
            Assert.Null(items[2].SegmentKind);
        }
    }

    public sealed class ScenarioContextAndTimeDateDueTogether
    {
        // Given: both deferrals due the same unit
        /// <summary>AC9 — the drain tiebreak</summary>
        [Fact]
        public async Task ContextPrecedesTimeDate()
        {
            var tts = new FakeTtsSegmentSource();
            var chain = new OrchestratorBuilder().WithTts(tts).Build();
            var content = new ContextSegmentFacts("Sunny and mild.", chain.Time.GetUtcNow().AddMinutes(10));
            chain.Queue.Enqueue(SpeechDeferralKind.Context, "test: weather due", discriminator: "weather", context: content);
            chain.Queue.Enqueue(SpeechDeferralKind.TimeDate, "test: time due");

            await PullAsync(chain.Orchestrator);

            Assert.Equal(SegmentKind.ContextSegment, tts.Requests[0].Kind);
            Assert.Equal(SegmentKind.TimeDate, tts.Requests[1].Kind);
        }
    }
}
