// STORY-320 — The boundary respects the backlog (gh-#469 · SPEC F124.1-.3, PLAN T266-T268)
//
// This file is T266's own slice: the ladder's classification widens to treat a queue crossing the
// boundary as a straddle (SPEC F124.1) — a QueuedAhead that alone spans UntilBoundary settles
// Straddle, never CeremonyOnly, regardless of how little (or negative) room a NEW candidate would
// otherwise leave. AC2 (the held sign-on's Due re-stamp, F124.2, T267) and AC3 (the CeremonyOnly
// drain instant counting QueuedAhead, F124.3, T268) are separate, later slices — pending below,
// unchanged from the pre-T266 scaffold, until their own tasks land.
//
// Honest scope: SignOff/SignOn cannot reach the widened classification AT ALL yet. Queue-crossing
// forces DesiredEffectiveLength deeply negative, which is always below MusicSelectionPolicy.MusicFloor
// — and for those two kinds, Orchestrator.ShouldDeclineFinalUnit's decline path still preempts
// MusicSelectionPolicy entirely, hard-coding the CeremonyOnly literal (Orchestrator.cs's
// TryServeCeremonyOnlyUnitAsync) before the widened ladder below is ever consulted. That is exactly
// the shape that produced the first-night incident's Loki line, and it still reproduces today by
// design: Story303_StraddleHandoff.cs:139-170's BelowTheFloorDeclinePathHardCodesCeremonyOnly fact is
// the live counter-fact, pinning that a QueuedAheadMs=200_000/45s-boundary SignOff fit still declines
// and logs rung=CeremonyOnly, unchanged by this file. T267 owns wiring the decline gate to respect
// this classification; only then does a real handoff boundary reach it.
//
// Both scenarios below therefore arm a StationId deferral rather than a SignOff/SignOn — StationId is
// never declined by ShouldDeclineFinalUnit (handoff kinds only), so every fit here reaches
// MusicSelectionPolicy.SelectMusicCandidateAsync unconditionally, exercising the classification
// widening in isolation from T267/T268's own decline/hold-set region. Story303_StraddleHandoff.cs's
// own BelowFloorNotDeclinedStillClassifiesCeremonyOnlyViaPolicy fact isolates the SAME floor arm the
// identical way.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBoundaryRespectsBacklog
{
    static readonly DateTimeOffset ClockStart = DateTimeOffset.Parse("2030-01-01T00:00:00Z");

    /// <summary>Mirrors Story303_StraddleHandoff's own MakeTrack verbatim.</summary>
    static MediaReference MakeTrack(string id, TimeSpan duration) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: (int)duration.TotalMilliseconds,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: null,
        Album: null,
        Genre: null,
        Year: null);

    static CadenceConfig CadenceOff => new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    /// <summary>Drives the real Orchestrator.GetNextAsync -> MusicSelectionPolicy.SelectMusicCandidateAsync
    /// seam through fakes — the same idiom Story303/Gh254/Gh300 established — with a CapturingLogger so
    /// the SPEC F111.5 rung token on the boundary-fit line can be asserted on directly.</summary>
    static Orchestrator BuildOrchestrator(
        FakeMediaCatalog catalog, SpeechDeferralQueue deferralQueue, TimeProvider clock, ILogger<Orchestrator> logger) =>
        new(
            new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            new FakeCadenceProvider(CadenceOff),
            new FakeRotationSettingsProvider(new RotationSettings()),
            new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance),
            new FakeTtsSegmentSource(),
            new FakeActivePersonaAccessor(),
            logger,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            deferralQueue,
            clock,
            new FakeBoundaryBiasProvider(TimeSpan.FromMinutes(10)));

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioAQueuedTailCrossingTheBoundaryClassifiesAsAStraddle
    {
        /// <summary>
        /// A StationId due in 100s (untilBoundary=100s, no SignOffLeadTime offset) with 150s already
        /// committed ahead of this pass (SPEC F124.1: QueuedAhead 150s &gt;= UntilBoundary 100s — the
        /// queued tail alone spans the boundary). Desired room for a NEW candidate is therefore deeply
        /// negative (100 - 150 = -50s, comfortably below the 90s floor) — exactly the shape that
        /// classified CeremonyOnly before this fix (Story303_StraddleHandoff's own
        /// BelowFloorNotDeclinedStillClassifiesCeremonyOnlyViaPolicy fact pins the pre-F124 floor-only
        /// arm) — but the queued tail is the crossing content here, not this pick's candidate.
        /// </summary>
        static async Task<CapturingLogger<Orchestrator>> RunAsync()
        {
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 100s",
                clock.GetUtcNow() + TimeSpan.FromSeconds(100));

            var pool = MakeTrack("still-plays", TimeSpan.FromMinutes(3));
            var catalog = FakeMediaCatalog.WithPool([pool]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            await orchestrator.GetNextAsync(new PlayoutContext([], QueuedAheadMs: 150_000), CancellationToken.None);

            return logger;
        }

        [Fact]
        public async Task The_rung_is_Straddle_when_QueuedAhead_spans_the_boundary()
        {
            var logger = await RunAsync();

            Assert.Contains(
                logger.Entries, entry => entry.Message.Contains("rung=Straddle", StringComparison.Ordinal));
        }

        [Fact]
        public async Task The_rung_never_classifies_as_CeremonyOnly()
        {
            var logger = await RunAsync();

            Assert.DoesNotContain(
                logger.Entries, entry => entry.Message.Contains("rung=CeremonyOnly", StringComparison.Ordinal));
        }
    }

    public static class ScenarioTheHeldSignOnsEligibilityFollowsTheTail
    {
        [Fact(Skip = "Pending T267 — see docs/PLAN.md")]
        public static void The_held_SignOn_Due_is_restamped_to_now_plus_queuedAhead()
        {
            // Given a sign-on held at a queue-crossing straddle
            // Then Due = max(Due, now + queuedAhead) — a one-seam hold cannot outlast
            // a multi-unit tail
            Assert.Fail("pending T267");
        }

        [Fact(Skip = "Pending T267 — see docs/PLAN.md")]
        public static void A_Due_already_past_the_estimate_is_not_moved_backward()
        {
            // max() semantics: re-stamping never makes a sign-on EARLIER.
            Assert.Fail("pending T267");
        }
    }

    public static class ScenarioTheCeremonyDrainInstantCountsTheQueue
    {
        [Fact(Skip = "Pending T268 — see docs/PLAN.md")]
        public static void The_drain_instant_includes_QueuedAhead_not_UntilBoundary_alone()
        {
            // Given a CeremonyOnly plan with a non-zero QueuedAhead
            Assert.Fail("pending T268");
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAnUnknownQueueEstimateDegradesToTodaysBehavior
    {
        [Fact]
        public async Task A_null_QueuedAhead_classifies_exactly_as_the_pre_F124_ladder()
        {
            // A StationId due in 50s with NO QueuedAheadMs supplied (PlayoutContext's default — the
            // "foreign airing, no feeder data" shape AC4 names) coalesces to QueuedAhead=0
            // (Orchestrator.BuildBoundaryFit's own queuedAheadMs ?? 0) — which can never satisfy
            // QueuedTailCrossesBoundary against a strictly-positive UntilBoundary. Desired room is
            // still below the 90s floor (50 - 0 = 50s), so the ladder falls through to EXACTLY the
            // pre-F124 floor-only comparison: CeremonyOnly, unchanged — the estimate only ever
            // tightens the ladder, never invents a crossing that was never reported.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 50s",
                clock.GetUtcNow() + TimeSpan.FromSeconds(50));

            var pool = MakeTrack("still-plays", TimeSpan.FromMinutes(3));
            var catalog = FakeMediaCatalog.WithPool([pool]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Contains(
                logger.Entries, entry => entry.Message.Contains("rung=CeremonyOnly", StringComparison.Ordinal));
        }

        [Fact(Skip = "Pending T268 — see docs/PLAN.md")]
        public static void A_null_QueuedAhead_leaves_the_drain_instant_unchanged()
        {
            Assert.Fail("pending T268");
        }
    }

    public static class ScenarioTheSignOffStillLeadsTheTail
    {
        [Fact(Skip = "Pending T267 — see docs/PLAN.md")]
        public static void The_SignOff_drains_at_the_next_seam_ahead_of_the_queued_content()
        {
            // The existing straddle sound, unchanged: the outgoing DJ's goodbye precedes
            // their own buffered tail; only the SIGN-ON waits for the drain.
            Assert.Fail("pending T267");
        }
    }
}
