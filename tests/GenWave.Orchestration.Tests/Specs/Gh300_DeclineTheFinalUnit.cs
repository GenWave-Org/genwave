// gh-#300 — Boundary-fit can't decline the final unit: a full track planned 30s before the sign-off
// was due, and the 2PM handoff aired at 2:05.
//
// BDD specification — xUnit. Drives the real Orchestrator.GetNextAsync through the same fakes
// Gh254_BoundaryFitSelection established. The fit gh-#254 shipped biases WHICH track fills a unit;
// it had no move for "plan no unit at all", so at the last pull before the ceremony every candidate
// overshot and the least-late one — a full ~3.5-minute track — went in front of the boundary anyway.
//
// Two halves, both specified here:
//   1. The decline. Under gh-#300's floor the ceremony becomes the unit, and no music is planned.
//      Planning early is not AIRING early: the ceremony queues behind audio still draining, so it
//      reaches air at the boundary. Never-silent is preserved by falling through to an ordinary
//      music unit whenever the ceremony renders nothing at all.
//   2. The record. The incident was reconstructible only from kokoro's render timestamps, because
//      BuildBoundaryFit logged nothing. Every fit now writes one INFORMATION line — Debug would
//      have been useless, since the fleet ships Information and above.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureDeclineTheFinalUnit
{
    static readonly DateTimeOffset ClockStart = DateTimeOffset.Parse("2030-01-01T00:00:00Z");

    /// <summary>The incident's own shape: a pool of ordinary ~3.5-minute tracks, none of which fits.</summary>
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

    static readonly HandoffContext Handoff = new("af_flip", "Flip", "Mic Cardioid");

    static Orchestrator BuildOrchestrator(
        FakeMediaCatalog catalog,
        SpeechDeferralQueue deferralQueue,
        TimeProvider clock,
        FakeTtsSegmentSource tts,
        ILogger<Orchestrator> logger) =>
        new(
            new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            new FakeCadenceProvider(CadenceOff),
            new FakeRotationSettingsProvider(new RotationSettings()),
            new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance),
            tts,
            new FakeActivePersonaAccessor(),
            logger,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            deferralQueue,
            clock,
            new FakeBoundaryBiasProvider(TimeSpan.FromMinutes(10)));

    /// <summary>
    /// The 19:59:29 pull, reproduced: the sign-off comes due in 30s (boundary 45s out, given the
    /// 15s lead time) while 200s of audio is already committed ahead of this pass. Desired room is
    /// therefore deeply NEGATIVE — the approach has already overshot the boundary — and the pool
    /// holds nothing but full-length tracks.
    /// </summary>
    static (Orchestrator Orchestrator, FakeMediaCatalog Catalog, FakeTtsSegmentSource Tts,
        CapturingLogger<Orchestrator> Logger, PlayoutContext Ctx) ArrangeTheIncident(
        bool ceremonyRendersNothing = false)
    {
        var clock = new FakeTimeProvider(ClockStart);
        var queue = new SpeechDeferralQueue(clock);
        queue.Enqueue(
            SpeechDeferralKind.SignOff, "test: handoff armed",
            clock.GetUtcNow() + TimeSpan.FromSeconds(30), Handoff);
        queue.Enqueue(
            SpeechDeferralKind.SignOn, "test: handoff armed",
            clock.GetUtcNow() + TimeSpan.FromSeconds(45), Handoff);

        var catalog = FakeMediaCatalog.WithPool([
            MakeTrack("full-length", TimeSpan.FromMinutes(3.5)),
            MakeTrack("also-full", TimeSpan.FromMinutes(4)),
        ]);
        var tts = new FakeTtsSegmentSource { AlwaysReturnNull = ceremonyRendersNothing };
        var logger = new CapturingLogger<Orchestrator>();

        return (
            BuildOrchestrator(catalog, queue, clock, tts, logger),
            catalog,
            tts,
            logger,
            new PlayoutContext([], QueuedAheadMs: 200_000));
    }

    public static class ScenarioTheLastUnitBeforeACeremonyIsTheCeremony
    {
        [Fact]
        public static async Task The_pull_returns_a_spoken_segment_rather_than_a_track()
        {
            var (orchestrator, _, _, _, ctx) = ArrangeTheIncident();

            var next = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.NotNull(next);
            Assert.StartsWith("tts:", next.MediaId, StringComparison.Ordinal);
        }

        [Fact]
        public static async Task The_segment_is_the_sign_off()
        {
            var (orchestrator, _, tts, _, ctx) = ArrangeTheIncident();

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.Contains(tts.Requests, request => request.Kind == SegmentKind.SignOff);
        }

        [Fact]
        public static async Task No_music_is_planned_at_all()
        {
            // The whole bug in one assertion: a full extra unit inside the last minute IS the slip.
            var (orchestrator, catalog, _, _, ctx) = ArrangeTheIncident();

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.Empty(catalog.RotationCallScopes);
        }

        [Fact]
        public static async Task Both_halves_of_the_ceremony_drain_together()
        {
            // The drain runs as-of the BOUNDARY, so the sign-on (due at it, 15s after the sign-off)
            // rides the same unit — the shape SignOffLeadTime's own remarks call the common case.
            var (orchestrator, _, tts, _, ctx) = ArrangeTheIncident();

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.Contains(tts.Requests, request => request.Kind == SegmentKind.SignOn);
        }

        [Fact]
        public static async Task The_sign_on_is_served_next_still_ahead_of_any_music()
        {
            var (orchestrator, catalog, _, _, ctx) = ArrangeTheIncident();

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            var second = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.NotNull(second);
            Assert.StartsWith("tts:", second.MediaId, StringComparison.Ordinal);
            Assert.Empty(catalog.RotationCallScopes);
        }
    }

    public static class ScenarioTheFitIsOnTheRecord
    {
        [Fact]
        public static async Task A_declined_fit_writes_its_own_line()
        {
            var (orchestrator, _, _, logger, ctx) = ArrangeTheIncident();

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.Contains(logger.Entries, entry => entry.Message.Contains("outcome=declined", StringComparison.Ordinal));
        }

        [Fact]
        public static async Task The_fit_line_is_information_not_debug()
        {
            // The fleet ships Information and above — a Debug line would be as invisible as none.
            var (orchestrator, _, _, logger, ctx) = ArrangeTheIncident();

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Information
                    && entry.Message.Contains("Boundary fit", StringComparison.Ordinal));
        }

        [Fact]
        public static async Task The_line_carries_the_terms_the_fit_reasoned_from()
        {
            var (orchestrator, _, _, logger, ctx) = ArrangeTheIncident();

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            var fitLine = Assert.Single(
                logger.Entries, entry => entry.Message.Contains("Boundary fit", StringComparison.Ordinal));
            Assert.Contains("queuedAhead=200.0s", fitLine.Message, StringComparison.Ordinal);
        }

        [Fact]
        public static async Task An_engaged_fit_records_what_the_sampler_did()
        {
            // Six minutes of room, nothing queued ahead: the fit engages and a track is chosen.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: handoff armed",
                clock.GetUtcNow() + TimeSpan.FromMinutes(6), Handoff);
            var catalog = FakeMediaCatalog.WithPool([MakeTrack("six-min", TimeSpan.FromMinutes(6))]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, new FakeTtsSegmentSource(), logger);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Contains(
                logger.Entries,
                entry => entry.Message.Contains("outcome=win", StringComparison.Ordinal)
                    || entry.Message.Contains("outcome=least-late", StringComparison.Ordinal));
        }
    }

    public static class ScenarioRoomForAUnitIsLeftAlone
    {
        [Fact]
        public static async Task A_boundary_six_minutes_out_still_gets_its_track()
        {
            // Above the floor the gh-#254 fit keeps its existing behavior, untouched.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: handoff armed",
                clock.GetUtcNow() + TimeSpan.FromMinutes(6), Handoff);
            var catalog = FakeMediaCatalog.WithPool([MakeTrack("six-min", TimeSpan.FromMinutes(6))]);
            var orchestrator = BuildOrchestrator(
                catalog, queue, clock, new FakeTtsSegmentSource(), new CapturingLogger<Orchestrator>());

            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(next);
            Assert.Equal("six-min", next.MediaId);
        }
    }

    public static class ScenarioOnlyAHandoffIsWorthATrack
    {
        [Fact]
        public static async Task A_future_dated_station_id_never_costs_a_music_unit()
        {
            // An ident is imaging — it can ride the next seam. Skipping a whole track for one would
            // trade a small blemish for a large one.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.StationId, "test: future-dated ident",
                clock.GetUtcNow() + TimeSpan.FromSeconds(30));
            var catalog = FakeMediaCatalog.WithPool([MakeTrack("full-length", TimeSpan.FromMinutes(3.5))]);
            var orchestrator = BuildOrchestrator(
                catalog, queue, clock, new FakeTtsSegmentSource(), new CapturingLogger<Orchestrator>());

            var next = await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: 200_000), CancellationToken.None);

            Assert.NotNull(next);
            Assert.Equal("full-length", next.MediaId);
        }
    }

    public static class ScenarioNeverSilentSurvivesTheDecline
    {
        [Fact]
        public static async Task A_ceremony_that_renders_nothing_falls_through_to_music()
        {
            // F6.3 stands: the decline may only ever ADD segments. If the whole ceremony drops, the
            // pull plans an ordinary music unit exactly as though the decline had never fired.
            var (orchestrator, _, _, _, ctx) = ArrangeTheIncident(ceremonyRendersNothing: true);

            var next = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.NotNull(next);
            Assert.DoesNotContain("tts:", next.MediaId, StringComparison.Ordinal);
        }
    }
}
