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
//
// SPEC F124.1 amendment (STORY-320, PLAN T267): ArrangeTheIncident's own numbers (200s already
// queued ahead of a 45s-out boundary) are, with hindsight, a QUEUE-CROSSING decline — the exact shape
// gh-#469 named. The decline itself is UNCHANGED (still fires, still airs the ceremony instead of a
// new track — see Orchestrator.ShouldDeclineFinalUnit's own remarks for why its condition never
// needed to widen), but TryServeCeremonyOnlyUnitAsync no longer hard-codes the CeremonyOnly rung or
// drains both ceremony pieces together for THIS shape — see the facts below that changed, and
// ScenarioTheNonCrossingDeclineIsUnchanged for the sibling shape (queued tail does NOT cross) that
// still pins the exact pre-F124 behavior this file always specified.

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

    /// <paramref name="cadence"/> defaults to <see cref="CadenceOff"/> (every pre-round-2 caller) — the
    /// R2-F1 repro below is the one caller that passes the station's DEFAULT cadence instead, since
    /// <c>BackAnnounceAfterEachTrack</c> being ON is exactly what the blind-peek repeated-decline defect
    /// needed to surface (see that fact's own remarks).
    static Orchestrator BuildOrchestrator(
        FakeMediaCatalog catalog,
        SpeechDeferralQueue deferralQueue,
        TimeProvider clock,
        FakeTtsSegmentSource tts,
        ILogger<Orchestrator> logger,
        CadenceConfig? cadence = null) =>
        new(
            new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            new FakeCadenceProvider(cadence ?? CadenceOff),
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
        public static async Task The_sign_on_is_held_rather_than_drained_with_the_sign_off()
        {
            // SUPERSEDES this fact's own pre-F124 name and assertion ("Both halves of the ceremony
            // drain together") — SPEC F124.1 (STORY-320, PLAN T267): ArrangeTheIncident's 200s already
            // queued ahead of this 45s-out boundary CROSSES it, so the paired SignOn is held rather
            // than draining alongside the SignOff in this same call. The pre-F124 shape aired both
            // pieces ahead of content that had not finished draining yet — see
            // ScenarioTheNonCrossingDeclineIsUnchanged below for the sibling shape where this exact,
            // original assertion still holds untouched.
            var (orchestrator, _, tts, _, ctx) = ArrangeTheIncident();

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.Contains(tts.Requests, request => request.Kind == SegmentKind.SignOff);
            Assert.DoesNotContain(tts.Requests, request => request.Kind == SegmentKind.SignOn);
        }

        [Fact]
        public static async Task The_second_pull_falls_through_to_music_the_sign_on_still_held()
        {
            // Round-1 review finding F1 superseded this fact's own pre-fix name and assertion ("the
            // sign-on is served next, still ahead of any music") — that was the hold lasting zero
            // seconds: the very next pull, same clock instant and same queuedAhead as the SignOff's own
            // decline, found the SignOn's re-stamped Due already satisfied by its own forced drain
            // instant. With NotBefore gating instead (checked against REAL wall-clock time, not this
            // pull's own forced "as of"), the queued tail has not had any real time to drain — the held
            // SignOn stays queued, and this pull falls through to an ordinary music unit instead, exactly
            // as if nothing were held at all. FeatureBoundaryRespectsBacklog's own repeated-pull chain
            // fact pins the same invariant at the queue level, across several pulls at this SAME instant.
            var (orchestrator, catalog, _, _, ctx) = ArrangeTheIncident();

            await orchestrator.GetNextAsync(ctx, CancellationToken.None); // the SignOff
            var second = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.NotNull(second);
            Assert.DoesNotContain("tts:", second.MediaId, StringComparison.Ordinal);
            Assert.NotEmpty(catalog.RotationCallScopes);
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

    public static class ScenarioTheNonCrossingDeclineIsUnchanged
    {
        // SPEC F124.1 (STORY-320, PLAN T267) only widens the QUEUE-CROSSING shape. A below-floor fit
        // whose already-queued runtime does NOT reach the boundary (20s queued, 80s out — well short of
        // crossing) takes exactly the pre-F124 decline path this file always specified: both ceremony
        // pieces drain together in the same call, and the rung stays the honest CeremonyOnly verdict —
        // BoundaryFitPlan.ClassifyOffToleranceRung agrees, since the queue never crosses here either.
        // Round-1 review finding F7 — split one assertion per fact, sharing this one arrange/act.
        static async Task<(MediaItem? Next, FakeTtsSegmentSource Tts, CapturingLogger<Orchestrator> Logger)>
            RunNonCrossingDeclineAsync()
        {
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: handoff armed",
                clock.GetUtcNow() + TimeSpan.FromSeconds(65), Handoff);
            queue.Enqueue(
                SpeechDeferralKind.SignOn, "test: handoff armed",
                clock.GetUtcNow() + TimeSpan.FromSeconds(80), Handoff);

            var catalog = FakeMediaCatalog.WithPool([MakeTrack("full-length", TimeSpan.FromMinutes(3.5))]);
            var tts = new FakeTtsSegmentSource();
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, tts, logger);

            var next = await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: 20_000), CancellationToken.None);

            return (next, tts, logger);
        }

        [Fact]
        public static async Task The_pull_returns_a_spoken_segment_rather_than_a_track()
        {
            var (next, _, _) = await RunNonCrossingDeclineAsync();

            Assert.NotNull(next);
            Assert.StartsWith("tts:", next.MediaId, StringComparison.Ordinal);
        }

        [Fact]
        public static async Task The_sign_off_drains()
        {
            var (_, tts, _) = await RunNonCrossingDeclineAsync();

            Assert.Contains(tts.Requests, r => r.Kind == SegmentKind.SignOff);
        }

        [Fact]
        public static async Task The_sign_on_drains_alongside_it()
        {
            var (_, tts, _) = await RunNonCrossingDeclineAsync();

            Assert.Contains(tts.Requests, r => r.Kind == SegmentKind.SignOn);
        }

        [Fact]
        public static async Task The_rung_logs_CeremonyOnly()
        {
            var (_, _, logger) = await RunNonCrossingDeclineAsync();

            Assert.Contains(
                logger.Entries, entry => entry.Message.Contains("rung=CeremonyOnly", StringComparison.Ordinal));
        }
    }

    public static class ScenarioTheHeldSignOnNeverReDeclinesBlindly
    {
        [Fact]
        public static async Task RepeatedPullsAtOneInstantDoNotLoopBackAnnounceAndMusicStillAppears()
        {
            // SPEC F124.1/F124.2 round-2 review finding F1 (Blocker) — reproduced WITH the station's
            // DEFAULT cadence (BackAnnounceAfterEachTrack ON). Every OTHER fact in this file arranges
            // CadenceOff, which hid this defect completely: with nothing to back-announce, a blindly
            // re-declined held SignOn rendered NOTHING at all (the SignOn itself already correctly
            // blocked by TryDequeueDue's own NotBefore gate), TryServeCeremonyOnlyUnitAsync returned
            // null, and GetNextAsync fell straight through to an ordinary music pick in that SAME pull
            // — no visible loop. With the default cadence, that identical repeated blind decline instead
            // Kicks a FRESH back-announce (for the same not-yet-advanced previousTrack) on every single
            // pull for as long as the hold lasts — dozens of fresh LLM+TTS renders, worse than the 2:05
            // incident this whole seam exists to fix (gh-#469's own field report).
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            var catalog = FakeMediaCatalog.WithPool([
                MakeTrack("intro", TimeSpan.FromMinutes(3)),
                MakeTrack("full-length", TimeSpan.FromMinutes(3.5)),
            ]);
            var tts = new FakeTtsSegmentSource();
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, tts, logger, cadence: new CadenceConfig());

            // Prime a real previousTrack first — an ordinary unit, nothing pending yet — so the
            // eventual ceremony unit below has an outgoing track to back-announce, matching the
            // incident's own shape (a real track already playing when the boundary crosses). The
            // DEFAULT cadence's own LeadIn plans TWO buffered items for this one unit (lead-in, then the
            // track) — drain BOTH pulls here so the buffer is genuinely empty before the chain below
            // starts, or the first chain pull would just dequeue the leftover buffered track without
            // ever reaching GetNextAsync's own peek/fit/decline logic at all.
            var primedLeadIn = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(primedLeadIn);
            var primedTrack = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(primedTrack);

            // Arm the incident's own crossing SignOff/SignOn — 200s already queued ahead of a boundary
            // 45s out (30s SignOff due + the 15s SignOffLeadTime).
            queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: handoff armed",
                clock.GetUtcNow() + TimeSpan.FromSeconds(30), Handoff);
            queue.Enqueue(
                SpeechDeferralKind.SignOn, "test: handoff armed",
                clock.GetUtcNow() + TimeSpan.FromSeconds(45), Handoff);

            // Repeated pulls at this SAME clock instant — exactly how a feeder pulling faster than real
            // audio drains would hammer this seam (the same "chain" idiom Story320's own repeated-pull
            // facts use).
            var ctx = new PlayoutContext([], QueuedAheadMs: 200_000);
            var chain = new List<MediaItem>();
            for (var pull = 0; pull < 10; pull++)
            {
                var next = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
                Assert.NotNull(next);
                chain.Add(next);
                if (!next.MediaId.StartsWith("tts:", StringComparison.Ordinal)) break; // reached real music
            }

            // Music genuinely appears within the chain — the hold never stalls playout outright. Under
            // round-2's blind peek this assertion is RED: every one of the 10 pulls above stays a fresh
            // back-announce, the chain never reaches a real track at all.
            Assert.Contains(chain, item => !item.MediaId.StartsWith("tts:", StringComparison.Ordinal));

            // No runaway repetition: at most the ceremony's own back-announce (for the primed track,
            // ahead of the SignOff) PLUS the immediately-following real unit's own back-announce (the
            // SAME still-not-yet-advanced previousTrack — ordinary cadence, not a repeat of the
            // decline) — never a THIRD, which is exactly what the blind re-peek used to manufacture once
            // per pull.
            var backAnnounceCount = tts.Requests.Count(r => r.Kind == SegmentKind.BackAnnounce);
            Assert.True(
                backAnnounceCount <= 2,
                $"expected at most 2 back-announce renders (the ceremony's own + the next real unit's " +
                $"own), got {backAnnounceCount} — a repeated-decline loop.");

            // The direct proof of the fix: the decline never repeats. PeekNextDue no longer reports the
            // still-held SignOn as "next up" once it is no longer the earliest UN-GATED entry, so
            // ShouldDeclineFinalUnit/TryServeCeremonyOnlyUnitAsync run exactly ONCE for the whole chain,
            // not once per pull.
            var declinedCount = logger.Entries.Count(
                entry => entry.Message.Contains("outcome=declined", StringComparison.Ordinal));
            Assert.Equal(1, declinedCount);
        }
    }
}
