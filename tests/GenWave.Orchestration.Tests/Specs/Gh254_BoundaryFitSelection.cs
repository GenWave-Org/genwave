// gh-#254 — Boundary-fit selection: land the last pre-handoff track near the top of the hour
//
// BDD specification — xUnit. Drives the real Orchestrator.GetNextAsync -> SelectMusicCandidateAsync
// fit through fakes, the same idiom Story198_BoundaryAwareSelection established (future-dated
// deferrals enqueued directly, standing in for the handoff producer's own trigger). Story198's two
// scenarios still pass unchanged — the fit refines the bias, it never replaces the soft-preference
// contract (SPEC F74.3: never a filter, pool never thins because of it).

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBoundaryFitSelection
{
    static readonly DateTimeOffset ClockStart = DateTimeOffset.Parse("2030-01-01T00:00:00Z");

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

    static Orchestrator BuildOrchestrator(
        FakeMediaCatalog catalog,
        SpeechDeferralQueue deferralQueue,
        TimeProvider clock,
        TimeSpan lookahead,
        CadenceConfig? cadence = null,
        IPatterDurationEstimator? estimator = null) =>
        new(
            new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            new FakeCadenceProvider(cadence ?? CadenceOff),
            new FakeRotationSettingsProvider(new RotationSettings()),
            new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance),
            new FakeTtsSegmentSource(),
            new FakeActivePersonaAccessor(),
            NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            deferralQueue,
            clock,
            new FakeBoundaryBiasProvider(lookahead),
            patterEstimator: estimator);

    public static class ScenarioQueuedAheadDriftIsAccountedFor
    {
        [Fact]
        public static async Task Three_minutes_already_queued_shrink_a_six_minute_target_to_three()
        {
            // Given an ident due in 6 minutes but 3 minutes of runtime already committed ahead of
            // this planning pass (the drift gh-#254's live repro named)
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 6 minutes",
                clock.GetUtcNow() + TimeSpan.FromMinutes(6));

            var sixMinute = MakeTrack("six-min", TimeSpan.FromMinutes(6));
            var threeMinute = MakeTrack("three-min", TimeSpan.FromMinutes(3));
            var catalog = FakeMediaCatalog.WithPool([sixMinute, threeMinute]); // 6-min sampled FIRST

            var orchestrator = BuildOrchestrator(catalog, queue, clock, TimeSpan.FromMinutes(10));
            var ctx = new PlayoutContext([], QueuedAheadMs: 180_000);

            // When the next track is selected
            var next = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            // Then the 3-minute track wins: its END lands on the due time once the queued 3 minutes
            // are accounted for. The pre-fit duration-vs-due comparison would have crowned the
            // 6-minute track (a perfect raw match) and run the boundary ~3 minutes late.
            Assert.NotNull(next);
            Assert.Equal(threeMinute.MediaId, next.MediaId);
        }
    }

    public static class ScenarioBreakPatterShiftsTheTarget
    {
        [Fact]
        public static async Task Expected_signoff_and_backannounce_time_is_reserved_before_the_boundary()
        {
            // Given a sign-off boundary 5 minutes out, with warmed historical estimates: this
            // station's back-announces run ~15s and Flip's sign-offs ~30s
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: handoff armed",
                clock.GetUtcNow() + TimeSpan.FromMinutes(5),
                new HandoffContext("af_flip", "Flip", "Mic Cardioid"));

            var estimator = new RollingPatterDurationEstimator();
            for (var i = 0; i < 3; i++)
            {
                estimator.ObserveRendered(SegmentKind.BackAnnounce, null, "default", TimeSpan.FromSeconds(15));
                estimator.ObserveRendered(SegmentKind.SignOff, "Flip", "af_flip", TimeSpan.FromSeconds(30));
            }

            // Target math: boundary = due + 15s sign-off lead = 315s out; minus 15s back-announce
            // and 30s sign-off = a 270s effective slot. The 275s track (270s after crossfade trim)
            // fits it exactly; the 350s decoy (345s effective) misses by 75s — outside even the
            // historical-tier tolerance — and would only fit the naive no-patter target.
            var decoy = MakeTrack("decoy", TimeSpan.FromSeconds(350));
            var fits = MakeTrack("fits", TimeSpan.FromSeconds(275));
            var catalog = FakeMediaCatalog.WithPool([decoy, fits]); // decoy sampled FIRST

            var cadence = new CadenceConfig
            {
                LeadInBeforeEachTrack = false,
                BackAnnounceAfterEachTrack = true,
                StationIdEveryNUnits = 0,
            };
            var orchestrator = BuildOrchestrator(
                catalog, queue, clock, TimeSpan.FromMinutes(10), cadence, estimator);

            // When the last pre-handoff track is selected
            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then the estimator-aware fit reserves the break's spoken time before the boundary.
            Assert.NotNull(next);
            Assert.Equal(fits.MediaId, next.MediaId);
        }
    }

    public static class ScenarioWithinToleranceIsAWin
    {
        [Fact]
        public static async Task The_first_sample_inside_the_window_is_kept_without_over_optimizing()
        {
            // Given an ident due in 4 minutes and a pool whose FIRST random sample already lands
            // within ±30s (5s off) while a later candidate would score a perfect 0s
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 4 minutes",
                clock.GetUtcNow() + TimeSpan.FromMinutes(4));

            var goodEnough = MakeTrack("good-enough", TimeSpan.FromSeconds(250)); // 245s effective, 5s off
            var perfect = MakeTrack("perfect", TimeSpan.FromSeconds(245));        // 240s effective, 0s off
            var catalog = FakeMediaCatalog.WithPool([goodEnough, perfect]);

            var orchestrator = BuildOrchestrator(catalog, queue, clock, TimeSpan.FromMinutes(10));

            // When selection runs
            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then the good-enough first sample wins AND sampling stopped at one draw — the
            // degenerate-pick guard: a win is a win, closest-fit leaderboards are what would
            // converge every hour onto the same track.
            Assert.NotNull(next);
            Assert.Equal(goodEnough.MediaId, next.MediaId);
            Assert.Single(catalog.RotationCallOrderedRecentIds);
        }
    }

    public static class ScenarioToleranceWidensWithLowConfidence
    {
        [Fact]
        public static async Task A_heuristic_tier_fit_accepts_what_an_exact_tier_fit_would_reject()
        {
            // Given a sign-off boundary 4 minutes out with a COLD estimator (chars-per-second
            // heuristic only — worst tier), and a first sample ~51s off the estimator-adjusted
            // target: outside the exact-tier ±30s, inside the heuristic-tier ±60s
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: handoff armed",
                clock.GetUtcNow() + TimeSpan.FromMinutes(4),
                new HandoffContext("af_flip", "Flip", null));

            var wide = MakeTrack("wide", TimeSpan.FromSeconds(300));
            var tight = MakeTrack("tight", TimeSpan.FromSeconds(245));
            var catalog = FakeMediaCatalog.WithPool([wide, tight]);

            var orchestrator = BuildOrchestrator(catalog, queue, clock, TimeSpan.FromMinutes(10));

            // When selection runs
            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then the wide-but-within-heuristic-tolerance first sample is accepted — a guess-grade
            // estimate must not pretend exact-tier precision, so the win window widens with it.
            Assert.NotNull(next);
            Assert.Equal(wide.MediaId, next.MediaId);
            Assert.Single(catalog.RotationCallOrderedRecentIds);
        }
    }

    public static class ScenarioOvershotApproachPicksLeastLate
    {
        [Fact]
        public static async Task With_only_long_tracks_the_least_late_one_airs_never_dead_air()
        {
            // Given a boundary only 1 minute out and nothing but long tracks — every pick overshoots
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 1 minute",
                clock.GetUtcNow() + TimeSpan.FromMinutes(1));

            var nineMinute = MakeTrack("nine-min", TimeSpan.FromMinutes(9));
            var fourMinute = MakeTrack("four-min", TimeSpan.FromMinutes(4));
            var catalog = FakeMediaCatalog.WithPool([nineMinute, fourMinute]);

            var orchestrator = BuildOrchestrator(catalog, queue, clock, TimeSpan.FromMinutes(10));

            // When selection runs
            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then a track still airs (never-silent — fit forward by choosing well, never stop
            // early) and it is the least-late of the sampled handful.
            Assert.NotNull(next);
            Assert.Equal(fourMinute.MediaId, next.MediaId);
        }
    }

    public static class ScenarioNoImminentBoundaryIsUntouched
    {
        [Fact]
        public static async Task Without_a_pending_deferral_selection_stays_the_single_plain_pick()
        {
            // Given no pending deferral at all (the everyday no-schedule shape)
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            var track = MakeTrack("plain", TimeSpan.FromMinutes(4));
            var catalog = FakeMediaCatalog.WithPool([track]);

            var orchestrator = BuildOrchestrator(catalog, queue, clock, TimeSpan.FromMinutes(10));

            // When selection runs — with a queued-ahead figure present, which must change nothing
            var next = await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: 120_000), CancellationToken.None);

            // Then exactly one catalog pick was issued: the fit engages ONLY in the approach window.
            Assert.NotNull(next);
            Assert.Equal(track.MediaId, next.MediaId);
            Assert.Single(catalog.RotationCallOrderedRecentIds);
        }
    }
}
