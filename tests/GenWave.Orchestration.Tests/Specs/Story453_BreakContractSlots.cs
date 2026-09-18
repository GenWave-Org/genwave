// STORY-453 — The break contract has one fact per cell (gh-#401 · SPEC F186 · PLAN T517–T519)
//
// AC10–AC17 (T517, GREEN below) and AC24 (manual, T519) — the second of three files
// FeatureBreakContract spans, split from Story453_BreakContract.cs purely for the ~300-line budget
// (csharp-best-practices). Shares that file's consts/helpers (PullAsync, PullManyAsync,
// BuildCrosstalkChain, StockReadyExchange, Handoff, ShowSlug) via the partial class. The drop cells
// (AC18–AC23, T518) live in Story453_BreakContractDrops.cs instead, un-skipped from the SAD PATH
// block this file used to carry. Manual is declared in THIS file — the Skip-prefix-law scanner
// resolves a Skip = <const> reference within its own file only, so a const backing a Skip= here
// must live here too.

using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Specs;

public static partial class FeatureBreakContract
{
    // Skip-prefix-law scanner (GenWave.Architecture.Tests) resolves a Skip = <const> reference within
    // its own file's syntax tree only — it does not follow a partial class across files. Declared
    // here, not in Story453_BreakContract.cs, because its one [Fact(Skip = ...)] usage lives in THIS
    // file (AC24, below).
    const string Manual = "manual: two follow-up issues named in SPEC F186.4 — review evidence on PR-2 (STORY-453)";

    public sealed class ScenarioAPooledStationId : IAsyncLifetime
    {
        readonly FakeTtsSegmentSource tts = new();
        MediaItem? item;

        // Given: catalog with a ready station-id asset
        public async Task InitializeAsync()
        {
            var cadence = Cadence(leadIn: false, backAnnounce: false, stationIdEvery: 1);
            var catalog = new FakeMediaCatalog(TestData.MakeTrackRef("t1")) { ImagingPoolResult = TestData.MakeTrackRef("pooled-station-id") };
            var chain = new OrchestratorBuilder().WithCadence(cadence).WithCatalog(catalog).WithTts(tts).Build();

            await PullAsync(chain.Orchestrator); // unit 0 — the cadence has not hit yet
            item = await PullAsync(chain.Orchestrator); // unit 1 — the pooled station-id leads
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC10 — the pool hit is used</summary>
        [Fact]
        public void BuffersThePooledItem()
        {
            Assert.NotNull(item);
            Assert.Equal(SegmentKind.StationId, item.SegmentKind);
            Assert.Equal("pooled-station-id", item.MediaId);
        }

        /// <summary>AC10 — no render when the pool hits</summary>
        [Fact]
        public void SendsNoShowIdentRequest() => Assert.DoesNotContain(tts.Requests, r => r.Kind == SegmentKind.StationId);
    }

    public sealed class ScenarioAnEmptyStationIdPool
    {
        // Given: catalog without a station-id asset
        /// <summary>AC11 — the tts fake received the render</summary>
        [Fact]
        public async Task SendsAShowIdentRequest()
        {
            var cadence = Cadence(leadIn: false, backAnnounce: false, stationIdEvery: 1);
            var tts = new FakeTtsSegmentSource();
            var chain = new OrchestratorBuilder().WithCadence(cadence).WithTts(tts).Build();

            await PullAsync(chain.Orchestrator); // unit 0 — the cadence has not hit yet
            await PullAsync(chain.Orchestrator); // unit 1 — no pool row, a templated render is sent

            Assert.Contains(tts.Requests, r => r.Kind == SegmentKind.StationId);
        }
    }

    public sealed class ScenarioALateTimeDate
    {
        // Given: TimeDate due 10 s ago with a 90 s budget
        /// <summary>AC12 — the request says Late</summary>
        [Fact]
        public async Task CarriesFreshnessLate()
        {
            var tts = new FakeTtsSegmentSource();
            var chain = new OrchestratorBuilder().WithTts(tts).Build();
            chain.Queue.Enqueue(SpeechDeferralKind.TimeDate, "test: time due", chain.Time.GetUtcNow() - TimeSpan.FromSeconds(10));

            // The fixed 90s honesty threshold (Orchestrator.TimeDateHonestyThreshold) reads lateness as
            // (now + queuedAhead) - due — 10s of raw staleness alone never crosses it, so this fact's own
            // queued tail (85s) is what pushes total lateness (95s) past the threshold into Late.
            await PullAsync(chain.Orchestrator, new PlayoutContext([], QueuedAheadMs: 85_000));

            var request = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.TimeDate);
            Assert.Equal(TimeAnnouncementFreshness.Late, request.TimeDateFreshness);
        }
    }

    public sealed class ScenarioADueContextSegment
    {
        // Given: a Context deferral with provider key weather
        /// <summary>AC13 — the buffered item names the provider</summary>
        [Fact]
        public async Task CarriesTheProviderKey()
        {
            var tts = new FakeTtsSegmentSource();
            var chain = new OrchestratorBuilder().WithTts(tts).Build();
            var content = new ContextSegmentFacts("Sunny and mild, 70F.", chain.Time.GetUtcNow().AddMinutes(10));
            chain.Queue.Enqueue(SpeechDeferralKind.Context, "test: weather due", discriminator: "weather", context: content);

            await PullAsync(chain.Orchestrator);

            var request = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.ContextSegment);
            Assert.Equal("Sunny and mild, 70F.", request.ContextFacts);
        }
    }

    public sealed class ScenarioLeadInOn
    {
        // Given: cadence lead-in on
        /// <summary>AC14 — lead-in sits last</summary>
        [Fact]
        public async Task IsTheLastSpokenItemBeforeTheTrack()
        {
            var cadence = Cadence(leadIn: true, backAnnounce: false, stationIdEvery: 1);
            var chain = new OrchestratorBuilder().WithCadence(cadence).Build();

            await PullManyAsync(chain.Orchestrator, 2); // unit 0 — lead-in, track (the cadence has not hit yet)
            var items = await PullManyAsync(chain.Orchestrator, 3); // unit 1 — station id, lead-in, track

            Assert.Equal(SegmentKind.StationId, items[0].SegmentKind);
            Assert.Equal(SegmentKind.LeadIn, items[1].SegmentKind);
            Assert.Null(items[2].SegmentKind);
        }
    }

    public sealed class ScenarioACeremonyOnlyUnit : IAsyncLifetime
    {
        readonly FakeTtsSegmentSource tts = new();
        FakeMediaCatalog? catalog;
        MediaItem? item;

        // Given: a SignOff below the music floor and a vendable crosstalk
        public async Task InitializeAsync()
        {
            var cadence = Cadence(leadIn: true, backAnnounce: false, stationIdEvery: 0);
            var assetPath = Path.Combine(Path.GetTempPath(), "genwave-story453-ac15-crosstalk.wav");
            var (chain, planner, resolvedCatalog) = BuildCrosstalkChain(cadence, tts);
            catalog = resolvedCatalog;
            StockReadyExchange(planner, assetPath);

            chain.Queue.Enqueue(SpeechDeferralKind.SignOff, "test: ceremony-only decline", chain.Time.GetUtcNow() + TimeSpan.FromSeconds(30), Handoff);

            item = await PullAsync(chain.Orchestrator, new PlayoutContext([], QueuedAheadMs: 200_000));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC15 — it buffers no crosstalk</summary>
        [Fact]
        public void BuffersNoCrosstalk()
        {
            Assert.NotNull(item);
            Assert.NotEqual(SegmentKind.Crosstalk, item.SegmentKind);
        }

        /// <summary>AC15 — it buffers no lead-in. Proved on the render seam, not the drained item (F1,
        /// round 2): the ceremony-only branch returns the drained SignOff before Orchestrator ever
        /// reaches its lead-in slot (src/GenWave.Orchestration/Orchestrator.cs:1589), so the drained
        /// item is never LeadIn regardless of whether a lead-in was rendered underneath it — only the
        /// tts fake's own request log can tell the two apart.</summary>
        [Fact]
        public void BuffersNoLeadIn() => Assert.DoesNotContain(tts.Requests, r => r.Kind == SegmentKind.LeadIn);

        /// <summary>AC15 — it buffers no track</summary>
        [Fact]
        public void BuffersNoTrack()
        {
            Assert.NotNull(catalog);
            Assert.Empty(catalog.RotationCallScopes);
        }
    }

    public sealed class ScenarioACeremonyOnlyUnitWithAPreviousTrack
    {
        // Given: the same unit with back-announce on (Dean: stays)
        /// <summary>AC16 — the outgoing DJ signs off the last track</summary>
        [Fact]
        public async Task BuffersABackAnnounceBeforeTheSignOff()
        {
            var cadence = Cadence(leadIn: false, backAnnounce: true, stationIdEvery: 0);
            var chain = new OrchestratorBuilder().WithCadence(cadence).WithLookahead(TimeSpan.FromMinutes(10)).Build();

            await PullAsync(chain.Orchestrator); // primes the previous track

            chain.Queue.Enqueue(SpeechDeferralKind.SignOff, "test: ceremony-only decline", chain.Time.GetUtcNow() + TimeSpan.FromSeconds(30), Handoff);
            var items = await PullManyAsync(chain.Orchestrator, 2, new PlayoutContext([], QueuedAheadMs: 200_000));

            Assert.Equal(SegmentKind.BackAnnounce, items[0].SegmentKind);
            Assert.Equal(SegmentKind.SignOff, items[1].SegmentKind);
        }
    }

    public sealed class ScenarioAStraddlingTrackWithASignOffPending
    {
        // Given: the queued tail alone crosses the boundary (SPEC F186.2(a)) — same below-floor SignOff
        // shape as AC15/AC16, but with a SignOn also pending so HoldSignOnPastQueuedTail has a live
        // target. This is TryServeCeremonyOnlyUnitAsync's own decline-Straddle rung, not
        // GetNextAsync's track-selection straddle branch (that branch never sets NotBefore — see
        // CaptureCrossingTrackForHeldSignOn's own remarks) — no catalog/crossing-track wiring is
        // needed since next is always null on this path.
        /// <summary>AC17 — NotBefore at or after now plus the tail</summary>
        [Fact]
        public async Task HoldsTheSignOnPastTheQueuedTail()
        {
            var chain = new OrchestratorBuilder().WithLookahead(TimeSpan.FromMinutes(10)).Build();

            var signOffDue = chain.Time.GetUtcNow() + TimeSpan.FromSeconds(30);
            var signOnDue = signOffDue + TimeSpan.FromSeconds(15);
            chain.Queue.Enqueue(SpeechDeferralKind.SignOff, "test: straddle decline", signOffDue, Handoff);
            chain.Queue.Enqueue(SpeechDeferralKind.SignOn, "test: straddle decline", signOnDue, Handoff);

            var pullInstant = chain.Time.GetUtcNow();
            await PullAsync(chain.Orchestrator, new PlayoutContext([], QueuedAheadMs: 200_000)); // the declined ceremony's sign-off piece

            var held = chain.Queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(held);
            Assert.Equal(pullInstant + TimeSpan.FromSeconds(200), held.NotBefore);
        }
    }

    public sealed class ScenarioTheFollowUpsOnPrTwo
    {
        // Given: PR-2's body (manual)
        /// <summary>AC24 — announcements on a ceremony-only unit (gh-#806); ad in a straddle break (gh-#807)</summary>
        [Fact(Skip = Manual)] public void TwoIssuesExistInProjectThree() => throw new NotImplementedException(Manual);
    }
}
