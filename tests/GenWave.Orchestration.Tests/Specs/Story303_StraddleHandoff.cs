// STORY-303 — The straddle handoff (F111, gh-#320, closes gh-#300)

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureStraddleHandoff
{
    static readonly DateTimeOffset ClockStart = DateTimeOffset.Parse("2030-01-01T00:00:00Z");

    /// <summary>Mirrors Gh254_BoundaryFitSelection/Gh300_DeclineTheFinalUnit's own track builder verbatim.
    /// <paramref name="artist"/> defaults to null (every pre-T235 caller) — only the F111.3 back-announce-
    /// capture facts below need a real one.</summary>
    static MediaReference MakeTrack(string id, TimeSpan duration, string? artist = null) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: (int)duration.TotalMilliseconds,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: artist,
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

    /// <summary>Drives the real Orchestrator.GetNextAsync -> MusicSelectionPolicy.SelectMusicCandidateAsync
    /// seam through fakes — the same idiom Gh254/Gh300 established — with a CapturingLogger so the
    /// SPEC F111.5 rung token on the boundary-fit line can be asserted on directly. <paramref name="tts"/>
    /// defaults to a fresh double (every pre-T235 caller); the straddle facts below pass their own so
    /// they can inspect <see cref="FakeTtsSegmentSource.Requests"/> after the run.</summary>
    static Orchestrator BuildOrchestrator(
        FakeMediaCatalog catalog, SpeechDeferralQueue deferralQueue, TimeProvider clock, ILogger<Orchestrator> logger,
        FakeTtsSegmentSource? tts = null) =>
        new(
            new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            new FakeCadenceProvider(CadenceOff),
            new FakeRotationSettingsProvider(new RotationSettings()),
            new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance),
            tts ?? new FakeTtsSegmentSource(),
            new FakeActivePersonaAccessor(),
            logger,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            deferralQueue,
            clock,
            new FakeBoundaryBiasProvider(TimeSpan.FromMinutes(10)));

    /// <summary>Shared straddle-boundary deferral setup for the ScenarioSignOffTrackSignOnInThatOrder/
    /// ScenarioDegradePerPiece facts below: a SignOff due far enough out (6 minutes) that the desired
    /// room clears the music floor even after a 9-minute crossing candidate misses tolerance (mirrors
    /// ScenarioTheLaddersMiddleRung.NothingFitsWithRoomAboveTheFloorSelectsStraddle's own numbers), and
    /// a paired SignOn due 15 seconds later (mirrors SignOffLeadTime, though nothing here enforces that
    /// relationship directly — the queue is seeded by hand).</summary>
    static (DateTimeOffset SignOffDue, DateTimeOffset SignOnDue) ArmStraddleCeremony(SpeechDeferralQueue queue, TimeProvider clock)
    {
        var signOffDue = clock.GetUtcNow() + TimeSpan.FromMinutes(6);
        var signOnDue = signOffDue + TimeSpan.FromSeconds(15);
        queue.Enqueue(SpeechDeferralKind.SignOff, "test: straddle", signOffDue, Handoff);
        queue.Enqueue(SpeechDeferralKind.SignOn, "test: straddle", signOnDue, Handoff);
        return (signOffDue, signOnDue);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheLaddersMiddleRung
    {
        [Fact]
        public async Task NothingFitsWithRoomAboveTheFloorSelectsStraddle()
        {
            // BoundaryFitPlan with no candidate within tolerance and desired length ≥ the music
            // floor (90s) ⇒ policy outcome Straddle. An ident due in 4 minutes leaves ~240s of
            // desired room; the only pool track misses tolerance by nearly 5 minutes, so the
            // ladder's middle rung — never a decline — fires.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 4 minutes",
                clock.GetUtcNow() + TimeSpan.FromMinutes(4));

            var tooLong = MakeTrack("too-long", TimeSpan.FromMinutes(9)); // 535s effective, ~295s off
            var catalog = FakeMediaCatalog.WithPool([tooLong]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Never-silent still holds — the least-late sample still airs — but the rung the fit
            // line records is Straddle, not the pre-T234 undifferentiated "least-late".
            Assert.NotNull(next);
            Assert.Equal(tooLong.MediaId, next.MediaId);
            Assert.Contains(
                logger.Entries, entry => entry.Message.Contains("rung=Straddle", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AFittingCandidateStillSelectsFit()
        {
            // The shipped gh-#254 path is byte-identical (AC5) — existing fit specs pass
            // unmodified; this fact pins the rung boundary from the other side: a within-tolerance
            // candidate still reports Fit, never Straddle, even though both rungs share the same
            // "a track airs" shape.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 4 minutes",
                clock.GetUtcNow() + TimeSpan.FromMinutes(4));

            var goodEnough = MakeTrack("good-enough", TimeSpan.FromSeconds(250)); // 245s effective, 5s off
            var catalog = FakeMediaCatalog.WithPool([goodEnough]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(next);
            Assert.Equal(goodEnough.MediaId, next.MediaId);
            Assert.Contains(
                logger.Entries, entry => entry.Message.Contains("rung=Fit", StringComparison.Ordinal));
        }

        // SUPERSEDES this scenario's own pre-F124 name and assertion ("hard-codes CeremonyOnly") —
        // SPEC F124.1 (STORY-320, PLAN T267). T234 review finding F2 originally pinned the DECLINE
        // path's own hard-coded CeremonyOnly literal here; T266 widened MusicSelectionPolicy's own
        // classifier to call this exact shape (200s already queued ahead of a boundary 45s out) a
        // Straddle instead, and T267 wired TryServeCeremonyOnlyUnitAsync to consult that SAME
        // classifier rather than hard-coding — see that method's own remarks for why
        // ShouldDeclineFinalUnit's condition never needed to change (crossing already implies
        // below-floor for a handoff kind). The DECLINE still fires and the ceremony still airs instead
        // of a new track (nothing about THAT changed) — but the log line now reports the honest rung,
        // and the paired SignOn is held rather than drained in this same call. See
        // BelowFloorNotDeclinedStillClassifiesCeremonyOnlyViaPolicy for the StationId/TimeDate sibling
        // (never decline-eligible), unaffected by this change. Round-1 review finding F7 — split one
        // assertion per fact, sharing this one arrange/act.
        static async Task<(MediaItem? Next, CapturingLogger<Orchestrator> Logger, FakeTtsSegmentSource Tts)>
            RunQueueCrossingDeclineAsync()
        {
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: handoff armed",
                clock.GetUtcNow() + TimeSpan.FromSeconds(30), Handoff);
            queue.Enqueue(
                SpeechDeferralKind.SignOn, "test: handoff armed",
                clock.GetUtcNow() + TimeSpan.FromSeconds(45), Handoff);

            var catalog = FakeMediaCatalog.WithPool([MakeTrack("full-length", TimeSpan.FromMinutes(3.5))]);
            var logger = new CapturingLogger<Orchestrator>();
            var tts = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger, tts);

            var next = await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: 200_000), CancellationToken.None);

            return (next, logger, tts);
        }

        [Fact]
        public async Task QueueCrossingDeclineStillAirsTheCeremonyNotMusic()
        {
            var (next, _, _) = await RunQueueCrossingDeclineAsync();

            Assert.NotNull(next);
            Assert.StartsWith("tts:", next.MediaId, StringComparison.Ordinal);
        }

        [Fact]
        public async Task QueueCrossingDeclineLogsOutcomeDeclined()
        {
            var (_, logger, _) = await RunQueueCrossingDeclineAsync();

            Assert.Contains(
                logger.Entries, entry => entry.Message.Contains("outcome=declined", StringComparison.Ordinal));
        }

        [Fact]
        public async Task QueueCrossingDeclineLogsRungStraddle()
        {
            var (_, logger, _) = await RunQueueCrossingDeclineAsync();

            Assert.Contains(
                logger.Entries, entry => entry.Message.Contains("rung=Straddle", StringComparison.Ordinal));
        }

        [Fact]
        public async Task QueueCrossingDeclineNeverLogsRungCeremonyOnly()
        {
            var (_, logger, _) = await RunQueueCrossingDeclineAsync();

            Assert.DoesNotContain(
                logger.Entries, entry => entry.Message.Contains("rung=CeremonyOnly", StringComparison.Ordinal));
        }

        [Fact]
        public async Task QueueCrossingDeclineHoldsThePairedSignOn()
        {
            // The paired SignOn is HELD, not drained in this same unit (SPEC F124.1's fix to the
            // pre-F124 inversion).
            var (_, _, tts) = await RunQueueCrossingDeclineAsync();

            Assert.DoesNotContain(tts.Requests, r => r.Kind == SegmentKind.SignOn);
        }

        [Fact]
        public async Task BelowFloorNotDeclinedStillClassifiesCeremonyOnlyViaPolicy()
        {
            // T234 review finding F1(a)/F2: a StationId deferral is NEVER declined by
            // ShouldDeclineFinalUnit (handoff kinds only, by design) even when the room in front of it
            // falls below MusicSelectionPolicy.MusicFloor — this fit reaches
            // MusicSelectionPolicy.SelectMusicCandidateAsync every time, and its own
            // ClassifyOffToleranceRung arm is what has to report CeremonyOnly here, on the ladder's
            // ordinary "least-late" line, not the decline path's hard-coded literal. Before this fact
            // existed, ClassifyOffToleranceRung's CeremonyOnly arm could be mutated to always return
            // Straddle with every one of this project's facts still green (F2) — this is the fact that
            // makes that mutation red.
            //
            // A StationId due in 200s, with 130s already queued ahead, leaves 70s of desired room —
            // under the 90s floor — and the only pool track misses tolerance by minutes, so the ladder
            // falls through to its off-tolerance classification: CeremonyOnly, not Straddle.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 200s",
                clock.GetUtcNow() + TimeSpan.FromSeconds(200));

            var tooLong = MakeTrack("still-plays", TimeSpan.FromMinutes(5)); // 295s effective, ~225s off
            var catalog = FakeMediaCatalog.WithPool([tooLong]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            var next = await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: 130_000), CancellationToken.None);

            // Music still airs — a below-floor StationId fit is never declined (F1(a)) — but the fit
            // line's rung is CeremonyOnly, off the policy's own classifier, on its "least-late" line.
            Assert.NotNull(next);
            Assert.Equal(tooLong.MediaId, next.MediaId);
            Assert.Contains(
                logger.Entries,
                entry => entry.Message.Contains("outcome=least-late", StringComparison.Ordinal)
                    && entry.Message.Contains("rung=CeremonyOnly", StringComparison.Ordinal));
        }

        [Fact]
        public async Task DesiredExactlyAtFloorIsStraddleAndNotDeclined()
        {
            // T234 review finding F5 — pins the 90s edge at BOTH sites the floor is compared against
            // (BoundaryFitPlan.IsBelowFloor, called from ClassifyOffToleranceRung AND
            // Orchestrator.ShouldDeclineFinalUnit as of F3's fix) in ONE fact, since F3 found those two
            // comparisons hand-written as complements rather than sharing one predicate. A SignOn due
            // in 200s with 110s already queued ahead leaves exactly 90s of desired room — the floor
            // itself, on the ">= floor" (Straddle) side of the ">=" convention.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.SignOn, "test: due in 200s",
                clock.GetUtcNow() + TimeSpan.FromSeconds(200));

            var tooLong = MakeTrack("crosses-anyway", TimeSpan.FromMinutes(5)); // 295s effective, 205s off
            var catalog = FakeMediaCatalog.WithPool([tooLong]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            var next = await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: 110_000), CancellationToken.None);

            // NOT declined: a SignOn exactly at the floor still takes the ordinary music path (proof
            // ShouldDeclineFinalUnit's own IsBelowFloor call reads ">= floor" as "not below"), and the
            // policy classifies the very same edge as Straddle, not CeremonyOnly.
            Assert.NotNull(next);
            Assert.Equal(tooLong.MediaId, next.MediaId);
            Assert.Contains(
                logger.Entries, entry => entry.Message.Contains("rung=Straddle", StringComparison.Ordinal));
        }
    }

    public sealed class ScenarioSignOffTrackSignOnInThatOrder
    {
        [Fact]
        public async Task TheStraddleUnitAirsSignOffThenTheCrossingTrack()
        {
            // Straddle outcome ⇒ this unit's buffer is [SignOff piece, crossing track]; SignOn is NOT
            // in it. GetNextAsync plans the WHOLE unit on the first call (returning only its first
            // item); a second call, with the buffer still non-empty, just pops the next one — no
            // re-planning, so this is the SAME unit's own second item, not a later seam's.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            ArmStraddleCeremony(queue, clock);

            var crossing = MakeTrack("crossing", TimeSpan.FromMinutes(9)); // 535s effective, misses tolerance by minutes
            var catalog = FakeMediaCatalog.WithPool([crossing]);
            var logger = new CapturingLogger<Orchestrator>();
            var tts = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger, tts);

            var first = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            var second = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(first);
            Assert.StartsWith("tts:signoff", first.MediaId, StringComparison.Ordinal);
            Assert.NotNull(second);
            Assert.Equal(crossing.MediaId, second.MediaId);

            // SignOn was neither aired nor even rendered this unit — held, not merely dropped.
            Assert.DoesNotContain(tts.Requests, r => r.Kind == SegmentKind.SignOn);
            Assert.Contains(
                logger.Entries, entry => entry.Message.Contains("rung=Straddle", StringComparison.Ordinal));
        }

        [Fact]
        public async Task SignOnDrainsAtTheSeamAfterTheCrossingTrack()
        {
            // The hold-set keeps SignOn queued through the straddle seam; the NEXT GetNextAsync
            // (once the clock has moved past both due times — the crossing track "played out") drains
            // it first, ahead of whatever music follows.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            ArmStraddleCeremony(queue, clock);

            var crossing = MakeTrack("crossing", TimeSpan.FromMinutes(9));
            var catalog = FakeMediaCatalog.WithPool([crossing]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None); // SignOff
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None); // crossing track

            clock.Advance(TimeSpan.FromMinutes(10)); // comfortably past both due times
            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(next);
            Assert.StartsWith("tts:signon", next.MediaId, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheSignOnCopyCanNameTheCrossingTrack()
        {
            // The handoff context captured at plan time carries the crossing track's title/artist onto
            // the held SignOn's own SegmentRequest — the WIRING half (SPEC F111.3); actual prompt
            // CONTENT is covered Tts-side (Story243's own file-split precedent — GenWave.Orchestration.Tests
            // has no reference to LlmPromptBuilder), in GenWave.Tts.Tests/Specs/Story303_StraddleHandoff.cs.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            ArmStraddleCeremony(queue, clock);

            var crossing = MakeTrack("crossing", TimeSpan.FromMinutes(9), artist: "The Testers");
            var catalog = FakeMediaCatalog.WithPool([crossing]);
            var logger = new CapturingLogger<Orchestrator>();
            var tts = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger, tts);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None); // SignOff
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None); // crossing track
            clock.Advance(TimeSpan.FromMinutes(10));
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None); // SignOn drains

            var signOnRequest = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.SignOn);
            Assert.Equal(crossing.Title, signOnRequest.CrossingTrackTitle);
            Assert.Equal(crossing.Artist, signOnRequest.CrossingTrackArtist);
        }
    }

    public sealed class ScenarioCrossesBoundaryGate
    {
        [Fact]
        public async Task AShortTrackNeverForcesTheSignOffEarly()
        {
            // T235 review findings F1/F5: BoundaryOutcome.Straddle alone used to force the SignOff
            // ahead of ANY off-tolerance pick clearing the floor — including a track far SHORTER than
            // desired, which cannot possibly cross the boundary. The reviewer's trace: forcing anyway
            // aired the sign-off 4-6 minutes early, with several more tracks still queued to play
            // before the real boundary, and the SignOn later lied about "still playing when you took
            // the chair." The fix (MusicSelectionResult.CrossesBoundary) gates the forced drain on the
            // candidate's OWN effective length actually reaching the boundary; a short pick takes the
            // ordinary path instead, and the SignOff airs near its own due (T234 baseline order) —
            // never forced by a pick that was never going to run past it.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            ArmStraddleCeremony(queue, clock); // SignOff due +6min, SignOn +6min15s ⇒ fit.UntilBoundary ≈ 375s

            var shortTrack = MakeTrack("short", TimeSpan.FromSeconds(65)); // 60s effective — nowhere near the ~375s boundary
            var catalog = FakeMediaCatalog.WithPool([shortTrack]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            var first = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // The ladder still classifies Straddle (desired room clears the floor) — but the short
            // track itself airs first, ordinary/unforced: no SignOff ahead of it.
            Assert.NotNull(first);
            Assert.Equal(shortTrack.MediaId, first.MediaId);
            Assert.Contains(
                logger.Entries, entry => entry.Message.Contains("rung=Straddle", StringComparison.Ordinal));

            // The SignOff is untouched — it did not drain early alongside the short pick.
            Assert.NotNull(queue.Peek(SpeechDeferralKind.SignOff));

            // Reproduces the reviewer's probe from the other side: advancing the clock to the SignOff's
            // own due (never forced earlier than this) still delivers it, ordinary/unforced.
            clock.Advance(TimeSpan.FromMinutes(6));
            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(next);
            Assert.StartsWith("tts:signoff", next.MediaId, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDegradePerPiece
    {
        [Fact]
        public async Task AFailedSignOffStillAirsTheCrossingTrackAndSignOn()
        {
            // F92.4: whichever piece rendered airs; music never waits; WARN + booth entry — the SAME
            // drop machinery every other handoff boundary already rides (LogHandoffDrop), never a new
            // straddle-only path.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            ArmStraddleCeremony(queue, clock);

            var crossing = MakeTrack("crossing", TimeSpan.FromMinutes(9));
            var catalog = FakeMediaCatalog.WithPool([crossing]);
            var logger = new CapturingLogger<Orchestrator>();
            var tts = new FakeTtsSegmentSource { ShouldReturnNull = req => req.Kind == SegmentKind.SignOff };
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger, tts);

            // The SignOff render failed — nothing for it airs, but the crossing track still airs
            // immediately, with no stall waiting on the dropped piece.
            var first = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(first);
            Assert.Equal(crossing.MediaId, first.MediaId);
            Assert.Contains(logger.Warnings, w => w.Contains("Handoff piece", StringComparison.OrdinalIgnoreCase));

            // The other piece of the ceremony — SignOn — still airs at its own seam, unaffected by the
            // SignOff's own drop (F92.4: each piece degrades independently).
            clock.Advance(TimeSpan.FromMinutes(10));
            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(next);
            Assert.StartsWith("tts:signon", next.MediaId, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NeverBackToBack()
        {
            // In a STRADDLE-classified unit (this fact's own scenario), SignOff and SignOn never
            // appear adjacent — the exact gh-#300 field report shape. Pulled across the whole straddle
            // (SignOff, crossing track, the held SignOn's own later seam, and whatever follows it), no
            // two consecutive items are ever the SignOff/SignOn pair.
            //
            // NOT a universal invariant across every unit shape (T235 review finding F7, scopes an
            // earlier version of this comment that overclaimed "structurally impossible" with no
            // qualifier): a due-NOW StationId (or any deferral due before the SignOff) is the earliest
            // pending entry, so PeekNextDue names IT, not the SignOff — GetNextAsync's untilDue<=0
            // guard then never builds a fit at all for that unit, and the straddle branch never even
            // sees pending.Kind==SignOff. If SignOff and SignOn both happen to already be due by that
            // SAME unit's drain, TryDequeueDue's own declaration-order tiebreak (SignOff before SignOn)
            // still delivers them back-to-back in that one call — a pre-existing shape this straddle
            // work neither introduced nor closes (it is the ordinary F92.6 "one-unit skew" ceremony
            // path, exercised by Story243's own NeitherPieceEverInterruptsATrack fact).
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            ArmStraddleCeremony(queue, clock);

            var crossing = MakeTrack("crossing", TimeSpan.FromMinutes(9));
            var catalog = FakeMediaCatalog.WithPool([crossing]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            var items = new List<MediaItem>
            {
                (await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None))!, // SignOff
                (await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None))!, // crossing track
            };
            clock.Advance(TimeSpan.FromMinutes(10)); // the crossing track "played out"
            items.Add((await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None))!); // SignOn
            items.Add((await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None))!); // next track

            for (var i = 0; i < items.Count - 1; i++)
            {
                var pairIsHandoffAdjacent =
                    (items[i].SegmentKind == SegmentKind.SignOff && items[i + 1].SegmentKind == SegmentKind.SignOn) ||
                    (items[i].SegmentKind == SegmentKind.SignOn && items[i + 1].SegmentKind == SegmentKind.SignOff);
                Assert.False(
                    pairIsHandoffAdjacent,
                    $"items[{i}] ({items[i].SegmentKind}) and items[{i + 1}] ({items[i + 1].SegmentKind}) aired back-to-back");
            }

            // The invariant is only meaningful if both pieces actually aired somewhere in the run.
            Assert.Contains(items, i => i.SegmentKind == SegmentKind.SignOff);
            Assert.Contains(items, i => i.SegmentKind == SegmentKind.SignOn);
        }
    }

    // ---------------------------------------------------------------------
    // Round-3 review — CaptureCrossingTrackForHeldSignOn's own notBefore carry
    // ---------------------------------------------------------------------

    public sealed class ScenarioCaptureCrossingTrackPreservesALiveHold
    {
        [Fact]
        public async Task TheGateSurvivesEnrichmentOfAPreviouslyHeldSignOn()
        {
            // Round-3 review — pins CaptureCrossingTrackForHeldSignOn's own
            // "notBefore: signOn.NotBefore" carry (Orchestrator.cs), now reachable: a SignOn already
            // HELD by an EARLIER, unrelated ceremony sits in the queue while a fresh, UNGATED SignOff
            // (a different, later boundary) heads an ordinary — never declined — straddle.
            // GetNextAsync's straddle branch enriches that SAME SignOn slot with the crossing track's
            // title/artist (SPEC F111.3); the PRE-EXISTING hold on it must survive that enrichment
            // untouched. Only reachable at all because PeekNextDue (the round-2 nucleus fix) correctly
            // skips the held entry and reports the fresh SignOff as "next up" instead — proof the two
            // fixes cooperate.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);

            // A SignOn already held from some earlier, unrelated ceremony — gated 5 minutes out. Its
            // own raw Due (1 minute) is EARLIER than the fresh SignOff below, but the gate keeps
            // PeekNextDue from ever reporting it.
            queue.Enqueue(
                SpeechDeferralKind.SignOn, "test: previously held", clock.GetUtcNow() + TimeSpan.FromMinutes(1), Handoff,
                notBefore: clock.GetUtcNow() + TimeSpan.FromMinutes(5));

            // A fresh, UNGATED SignOff for a LATER, unrelated boundary — mirrors ArmStraddleCeremony's
            // own numbers (6 minutes out, comfortably above the music floor on its own) so this is an
            // ORDINARY straddle, never a decline — the straddle branch, not TryServeCeremonyOnlyUnitAsync,
            // is what calls CaptureCrossingTrackForHeldSignOn.
            queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: fresh ceremony", clock.GetUtcNow() + TimeSpan.FromMinutes(6), Handoff);

            var crossing = MakeTrack("crossing", TimeSpan.FromMinutes(9)); // 535s effective — crosses the 6-minute boundary
            var catalog = FakeMediaCatalog.WithPool([crossing]);
            var orchestrator = BuildOrchestrator(catalog, queue, clock, new CapturingLogger<Orchestrator>());

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var enriched = queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(enriched);

            // The content enriched — proof CaptureCrossingTrackForHeldSignOn actually ran on this slot.
            Assert.Equal(crossing.Title, enriched.Handoff?.CrossingTrackTitle);

            // The PRE-EXISTING hold survived the enrichment untouched.
            Assert.Equal(ClockStart + TimeSpan.FromMinutes(5), enriched.NotBefore);
        }
    }

    // ---------------------------------------------------------------------
    // T235 review findings F2/F3 — the straddle branch's own reconciliation
    // ---------------------------------------------------------------------

    public sealed class ScenarioReconciliationDuringPlan
    {
        // GetNextAsync's straddle branch calls EnqueueHandoffCeremonyAsync a SECOND time — ahead of
        // EnqueuePatterAsync's own step 2.5 — purely to reconcile the ceremony's arm-once state against
        // whatever the schedule resolver says RIGHT NOW, before trusting the peeked SignOff as
        // forceable. ArmStraddleCeremony's manually-seeded queue (every other fact in this file) has no
        // schedule resolver wired at all, so it can never reproduce either finding below — these facts
        // need a REAL CachingScheduleResolver/ScheduleResolver/FakeScheduleStore chain
        // (Story243_DjsHandOffAudibly.cs's own BuildProductionChain idiom, reduced to just what these
        // two facts need).

        static readonly DayOfWeek Monday = new DateTimeOffset(2030, 1, 7, 0, 0, 0, TimeSpan.Zero).DayOfWeek;

        // Noon boundary, 5 minutes out — inside the 10-minute F74.3 window from the very first unit,
        // mirroring Story243's own JustBeforeNoon.
        static readonly DateTimeOffset JustBeforeNoon = new(2030, 1, 7, 11, 55, 0, TimeSpan.Zero);

        static ScheduleWeekSnapshot TwoDjSchedule(int betaStartMinute) => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: betaStartMinute, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: betaStartMinute, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        static FakePersonaStore TwoDjStore()
        {
            var now = DateTime.UnixEpoch;
            var store = new FakePersonaStore();
            store.Add(new Persona(10, "DJ Alpha", "", "", "af_alpha", now, now));
            store.Add(new Persona(20, "DJ Beta", "", "", "af_beta", now, now));
            return store;
        }

        // Round-2 review finding F4's own fact needs a THIRD persona (Beta's own boundary, once the
        // resolver's "current" rolls forward off Alpha, must have somewhere real to hand off to) —
        // ThreeDjSchedule/ThreeDjStore below, additive, every TwoDj* fact above untouched.
        static ScheduleWeekSnapshot ThreeDjSchedule(int betaStartMinute, int gammaStartMinute) => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: betaStartMinute, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: betaStartMinute, EndMinute: gammaStartMinute, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 3, Day: Monday, StartMinute: gammaStartMinute, EndMinute: 1440, PersonaId: 30, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        static FakePersonaStore ThreeDjStore()
        {
            var now = DateTime.UnixEpoch;
            var store = new FakePersonaStore();
            store.Add(new Persona(10, "DJ Alpha", "", "", "af_alpha", now, now));
            store.Add(new Persona(20, "DJ Beta", "", "", "af_beta", now, now));
            store.Add(new Persona(30, "DJ Gamma", "", "", "af_gamma", now, now));
            return store;
        }

        // Round-3 review — the two per-half SignOn ClearStale sites (Orchestrator.EnqueueHandoffCeremonyAsync)
        // each need their OWN reachable shape with a live hold already sitting in the slot:
        //
        //   - the GENERAL branch's ClearStale fires on "incoming is null" — a real persona-less next
        //     block (SPEC F91.1: PersonaId null = music-only), the literal fixture below.
        //   - the SAME-PERSONA/DIFFERENT-SHOW branch's ClearStale fires only on an UNRESOLVABLE incoming
        //     persona (boundaryAt.Value <= now is provably unreachable there — the outer window-exit
        //     check already excludes it under the SAME now/boundaryAt this branch reads) — reached below
        //     by removing the persona from the store mid-test (FakePersonaStore.Remove), the same
        //     "deleted out of band" shape ResolveHandoffPersonaAsync's own remarks already name.
        //
        // Reuses TwoDjStore (personas 10/20) for both — nothing here needs Gamma.

        static ScheduleWeekSnapshot NullPersonaNextBlockSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 725, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 3, Day: Monday, StartMinute: 725, EndMinute: 1440, PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        static ScheduleWeekSnapshot SamePersonaDifferentShowSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 723, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null, ShowId: 100),
            new ScheduleSegment(Id: 3, Day: Monday, StartMinute: 723, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null, ShowId: 200),
        ]);

        /// <paramref name="personaStore"/> defaults to <see cref="TwoDjStore"/> (every pre-round-2
        /// caller) — the R2-F4 fact below is the one caller that passes <see cref="ThreeDjStore"/>
        /// instead, since a re-arm-during-a-live-hold repro needs a THIRD persona for the resolver's
        /// own "current" to roll forward onto once real wall-clock time passes the FIRST boundary.
        static (Orchestrator Orchestrator, FakeScheduleStore ScheduleStore, FakeTimeProvider Time, SpeechDeferralQueue Queue)
            BuildChain(FakeMediaCatalog catalog, ScheduleWeekSnapshot snapshot, FakePersonaStore? personaStore = null)
        {
            var time = new FakeTimeProvider(JustBeforeNoon);
            var scheduleStore = new FakeScheduleStore(snapshot);
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));
            var caching = new CachingScheduleResolver(scheduleStore, resolver, new FakeScheduleSpecialStore());
            var queue = new SpeechDeferralQueue(time);
            var orchestrator = new Orchestrator(
                new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
                new FakeStationScopeProvider(new LibraryScope([1L])),
                new FakeCadenceProvider(CadenceOff),
                new FakeRotationSettingsProvider(new RotationSettings()),
                new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance),
                new FakeTtsSegmentSource(),
                new FakeActivePersonaAccessor(),
                NullLogger<Orchestrator>.Instance,
                new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
                queue,
                time,
                new FakeBoundaryBiasProvider(TimeSpan.FromMinutes(10)),
                scheduleResolver: caching,
                personaStore: personaStore ?? TwoDjStore());

            return (orchestrator, scheduleStore, time, queue);
        }

        [Fact]
        public async Task AScheduleWriteThatMovesTheBoundaryIsNeverForcedToTheStaleDue()
        {
            // T235 review finding F2: without the reconciledSignOff.Due == pending.Due guard, the code
            // forced this unit's drain to the RECONCILED SignOff's due even when reconciliation MOVED
            // the boundary — the field report: sign-off 6:45 early, plus the WRONG boundary's SignOn
            // stamped with THIS unit's crossing track.
            var crossing = MakeTrack("crossing-moved", TimeSpan.FromMinutes(9)); // 535s effective — comfortably crosses
            var catalog = FakeMediaCatalog.WithPool([crossing]);
            var (orchestrator, scheduleStore, time, queue) = BuildChain(catalog, TwoDjSchedule(betaStartMinute: 720)); // noon

            // Unit 1: nothing pending yet (fit is null on the very first call) — this unit's own step
            // 2.5 arms the ceremony for the noon boundary.
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            var armedSignOff = queue.Peek(SpeechDeferralKind.SignOff);
            Assert.NotNull(armedSignOff);

            // An admin edit lands between units: Beta now starts at 12:03, not noon — still inside the
            // window (8 minutes out from 11:55), so the reconciliation below RE-ARMS rather than clears.
            scheduleStore.SetSnapshot(TwoDjSchedule(betaStartMinute: 723));
            scheduleStore.RaiseWeekChanged();

            // Unit 2: peeks the STALE (noon) SignOff, picks the crossing track (Straddle, crosses the
            // stale boundary), then reconciles — and the reconciled SignOff now sits at a DIFFERENT due
            // (the moved boundary's own).
            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // The fix: never forced. The crossing track airs immediately, ordinary/unforced.
            Assert.NotNull(next);
            Assert.Equal(crossing.MediaId, next.MediaId);

            // The reconciled SignOff is untouched (not drained) and genuinely moved (a different due
            // than the one this unit peeked) — proof this was a reconciliation, not a no-op.
            var reconciledSignOff = queue.Peek(SpeechDeferralKind.SignOff);
            Assert.NotNull(reconciledSignOff);
            Assert.NotEqual(armedSignOff.Due, reconciledSignOff.Due);
            Assert.True(
                reconciledSignOff.Due > time.GetUtcNow(), "the reconciled sign-off must still be pending, not forced due");

            // The reconciled SignOn was never enriched with this unit's (wrong-boundary) crossing track.
            var reconciledSignOn = queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(reconciledSignOn);
            Assert.Null(reconciledSignOn.Handoff?.CrossingTrackTitle);
        }

        [Fact]
        public async Task AScheduleWriteThatRetractsTheBoundaryPlansAnOrdinaryUnit()
        {
            // T235 review finding F3 — the retraction half: the schedule write moves the boundary
            // clean OUT of the F74.3 window, so EnqueueHandoffCeremonyAsync's own ClearCeremony wipes
            // BOTH pieces on reconciliation. Nothing is left to force — this unit plans as an ordinary
            // music unit, exactly as if no ceremony had ever been in play.
            var crossing = MakeTrack("crossing-retract", TimeSpan.FromMinutes(9));
            var catalog = FakeMediaCatalog.WithPool([crossing]);
            var (orchestrator, scheduleStore, _, queue) = BuildChain(catalog, TwoDjSchedule(betaStartMinute: 720)); // noon

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None); // arms for noon
            Assert.NotNull(queue.Peek(SpeechDeferralKind.SignOff));

            // Beta now starts at 12:10 — 15 minutes out from 11:55, outside the 10-minute window.
            scheduleStore.SetSnapshot(TwoDjSchedule(betaStartMinute: 730));
            scheduleStore.RaiseWeekChanged();

            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(next);
            Assert.Equal(crossing.MediaId, next.MediaId);
            Assert.Null(queue.Peek(SpeechDeferralKind.SignOff));
            Assert.Null(queue.Peek(SpeechDeferralKind.SignOn));
        }

        [Fact]
        public async Task AHeldSignOnSurvivesTheBoundaryAndAirsAfterTheQueuedTailDrains()
        {
            // Round-1 review finding F2's own reproduction, on the REAL CachingScheduleResolver chain
            // (this class's own harness, not ArmStraddleCeremony's manually-seeded queue — the defect
            // lives in EnqueueHandoffCeremonyAsync's window-exit branch, which only ever runs off a
            // genuine schedule resolve). A queued tail requested LARGER than the F74.3 lookahead window
            // (20 minutes queued, 10-minute window) — round-2 review finding F5 clamps
            // HoldSignOnPastQueuedTail's own GATE to that SAME window (SPEC F124.6's own watch item: a
            // multi-hour backlog must never arm a hold arbitrarily far past the one window this class's
            // whole fit machinery reasons inside of), so the actual hold lands at 11:55 + 10min = 12:05,
            // not the raw 12:15 a naive "now + queuedAhead" would have used. The pre-fix (round-1) defect
            // this fact still reproduces: the SignOn survives past the boundary (real "now" > noon), the
            // resolver's own "current" flips to DJ Beta, EnqueueHandoffCeremonyAsync's window-exit fires
            // (Beta's own boundary — midnight — is nowhere near the 10-minute window), and its
            // ClearCeremony wipes the still-held, not-yet-airable SignOn outright: the incoming DJ never
            // signs on. The fix: a held deferral (NotBefore in the future) is LIVE, not stale, so it
            // survives that clear — and drains, ordinarily, once real wall-clock time actually reaches
            // the (now window-clamped) estimated drain instant.
            var tail = MakeTrack("tail-content", TimeSpan.FromMinutes(9));
            var catalog = FakeMediaCatalog.WithPool([tail]);
            var (orchestrator, _, time, queue) = BuildChain(catalog, TwoDjSchedule(betaStartMinute: 720)); // noon

            // Unit 1: nothing pending yet — arms the ceremony for noon (SignOff due 11:59:45, SignOn
            // due 12:00:00).
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(queue.Peek(SpeechDeferralKind.SignOff));

            // Unit 2: 20 minutes already queued ahead — comfortably crosses the 300s-out boundary, and
            // comfortably exceeds the 10-minute F74.3 window. The SignOff declines and airs; the paired
            // SignOn is held, its gate CLAMPED to the window bound (NotBefore = 11:55 + 10min = 12:05 —
            // never the raw 12:15 the unclamped 20-minute estimate would have produced).
            await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: (int)TimeSpan.FromMinutes(20).TotalMilliseconds), CancellationToken.None);
            var held = queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(held);
            Assert.Equal(JustBeforeNoon + TimeSpan.FromMinutes(10), held.NotBefore);

            // Real wall-clock time now crosses noon — well before the CLAMPED held estimate (12:05) —
            // while the queued tail is still nowhere near drained. The resolver's own "current" flips to
            // DJ Beta (running to midnight), pushing the next boundary far outside the window: this
            // unit's own step 2.5 fires EnqueueHandoffCeremonyAsync's window-exit branch, the exact call
            // round-1 wiped the held SignOn from.
            time.Advance(TimeSpan.FromMinutes(7)); // 11:55 -> 12:02 (past noon, still short of 12:05)
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(queue.Peek(SpeechDeferralKind.SignOn));

            // Real wall-clock time now reaches the (clamped) held estimate (12:05) — the SignOn is no
            // longer held back by anything, and the very next pull's ordinary (unforced) drain airs it.
            time.Advance(TimeSpan.FromMinutes(4)); // 12:02 -> 12:06
            var afterHold = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(afterHold);
            Assert.Equal(SegmentKind.SignOn, afterHold.SegmentKind);
        }

        [Fact]
        public async Task ARearmDuringALiveHoldPreservesTheGateRatherThanBypassingIt()
        {
            // SPEC F124.1/F124.2 round-2 review finding F4 — EnqueueHandoffCeremonyAsync's own SignOn
            // re-arm (the ordinary "boundary entered the F74.3 window" Enqueue call) must not silently
            // drop a LIVE hold already sitting on that queue slot. This reproduces the natural way a
            // re-arm lands mid-hold with no admin schedule edit at all: once real wall-clock time passes
            // the OLD (Alpha->Beta) boundary while the paired SignOn is still held behind a long queued
            // tail, the resolver's own "current" segment rolls forward to Beta — and once Beta's OWN
            // next boundary (Beta->Gamma) is itself inside the F74.3 window, this producer re-evaluates
            // and arms a FRESH Beta->Gamma ceremony, reusing the EXACT SAME (SignOn, null) queue slot
            // the still-held Alpha->Beta SignOn occupies.
            var crossing = MakeTrack("crossing", TimeSpan.FromMinutes(3));
            var catalog = FakeMediaCatalog.WithPool([crossing]);
            var (orchestrator, _, time, queue) = BuildChain(
                catalog, ThreeDjSchedule(betaStartMinute: 720, gammaStartMinute: 723), ThreeDjStore()); // noon, 12:03

            // Unit 1: arms the Alpha->Beta ceremony for noon.
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(queue.Peek(SpeechDeferralKind.SignOff));

            // Unit 2: 20 minutes already queued ahead — crosses. The SignOff declines and airs; the
            // paired (Beta) SignOn is held, gated to the F74.3 window bound: NotBefore = 11:55 + 10min =
            // 12:05 (SPEC F124.1's round-2 F5 clamp).
            await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: (int)TimeSpan.FromMinutes(20).TotalMilliseconds), CancellationToken.None);
            var held = queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(held);
            Assert.Equal(JustBeforeNoon + TimeSpan.FromMinutes(10), held.NotBefore);

            // Real wall-clock time now crosses noon — still short of the held estimate (12:05) — and
            // Beta's OWN next boundary (12:03) is now inside the 10-minute window: this unit's own step
            // 2.5 re-arms a FRESH Beta->Gamma ceremony over the SAME SignOn slot.
            time.Advance(TimeSpan.FromMinutes(6)); // 11:55 -> 12:01
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var reArmed = queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(reArmed);

            // The CONTENT correctly refreshed to the new, real boundary (Beta->Gamma, due 12:03) — this
            // is deliberately NOT "refuse to re-arm at all" (the rejected alternative): the fresh
            // content is correct and gets its own fair shot at airing.
            Assert.Equal(JustBeforeNoon + TimeSpan.FromMinutes(8), reArmed.Due);

            // But the GATE survived the re-arm — the still-draining Alpha->Beta tail's own hold (12:05)
            // is not silently bypassed just because a different ceremony's content now occupies this
            // slot; airing the Beta->Gamma sign-on a few seconds after 12:05 is still correct, airing it
            // the instant real time reaches its own (unguarded) Due of 12:03 — cutting over the
            // still-draining Alpha->Beta tail — would not have been.
            Assert.Equal(JustBeforeNoon + TimeSpan.FromMinutes(10), reArmed.NotBefore);
        }

        [Fact]
        public async Task TheGeneralBranchesClearStaleNeverErasesALiveHold()
        {
            // Round-3 review — pins the GENERAL branch's ClearStale (not the shared window-exit
            // ClearCeremony call, and not the same-persona-transition branch's own sibling below):
            // reached when EnqueueHandoffCeremonyAsync's "incoming is null" arm fires for a genuine
            // persona-less next block (SPEC F91.1). Reverting this ONE call site back to the blind
            // Clear must turn this fact red — the shared ClearCeremony call at window-exit is
            // deliberately never exercised here (Beta's own next block still resolves within the
            // window, so the outer gap check never short-circuits first).
            var tail = MakeTrack("tail-content", TimeSpan.FromMinutes(9));
            var catalog = FakeMediaCatalog.WithPool([tail]);
            var (orchestrator, _, time, queue) = BuildChain(catalog, NullPersonaNextBlockSchedule()); // noon, 12:05

            // Unit 1: arms the Alpha->Beta ceremony for noon.
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(queue.Peek(SpeechDeferralKind.SignOff));

            // Unit 2: 20 minutes already queued ahead — crosses. The SignOff declines and airs; the
            // paired (Beta) SignOn is held, gated to the window bound: NotBefore = 11:55 + 10min = 12:05.
            await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: (int)TimeSpan.FromMinutes(20).TotalMilliseconds), CancellationToken.None);
            var held = queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(held);
            Assert.Equal(JustBeforeNoon + TimeSpan.FromMinutes(10), held.NotBefore);

            // Real wall-clock time now crosses noon — still short of the held estimate (12:05). The
            // resolver's own "current" flips to Beta, whose OWN next block (the persona-less segment) is
            // itself inside the window: this producer's GENERAL branch re-evaluates, resolves a null
            // incoming persona, and calls ClearStale on the SAME (SignOn, null) slot the still-held Beta
            // sign-on occupies.
            time.Advance(TimeSpan.FromMinutes(6)); // 11:55 -> 12:01
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(queue.Peek(SpeechDeferralKind.SignOn));

            // Real wall-clock time now reaches the held estimate (12:05) — the sign-on still airs.
            time.Advance(TimeSpan.FromMinutes(5)); // 12:01 -> 12:06
            var afterHold = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(afterHold);
            Assert.Equal(SegmentKind.SignOn, afterHold.SegmentKind);
        }

        [Fact]
        public async Task TheSamePersonaBranchesClearStaleNeverErasesALiveHold()
        {
            // Round-3 review — pins the SAME-PERSONA/DIFFERENT-SHOW branch's OWN ClearStale (the
            // sibling of the general-branch fact above, a genuinely different call site). Reachable
            // ONLY via an unresolvable incoming persona (boundaryAt.Value <= now is provably dead code
            // here — the outer window-exit check already excludes it under the identical now/boundaryAt
            // this branch reads) — modeled the same way ResolveHandoffPersonaAsync's own remarks
            // describe: "deleted out of band" mid-test, via FakePersonaStore.Remove.
            var store = TwoDjStore();
            var tail = MakeTrack("tail-content", TimeSpan.FromMinutes(9));
            var catalog = FakeMediaCatalog.WithPool([tail]);
            var (orchestrator, _, time, queue) = BuildChain(catalog, SamePersonaDifferentShowSchedule(), store); // noon, 12:03

            // Unit 1: arms the Alpha->Beta ceremony for noon (Beta still resolvable at this point).
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(queue.Peek(SpeechDeferralKind.SignOff));

            // Unit 2: 20 minutes already queued ahead — crosses. The SignOff declines and airs; the
            // paired (Beta) SignOn is held, gated to the window bound: NotBefore = 11:55 + 10min = 12:05.
            await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: (int)TimeSpan.FromMinutes(20).TotalMilliseconds), CancellationToken.None);
            var held = queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(held);
            Assert.Equal(JustBeforeNoon + TimeSpan.FromMinutes(10), held.NotBefore);

            // Beta persona deleted out of band — the NEXT same-persona-different-show evaluation cannot
            // resolve the incoming half any more.
            store.Remove(20);

            // Real wall-clock time now crosses noon — still short of the held estimate (12:05). The
            // resolver's own "current" flips to Beta/show-A, whose OWN next block is Beta/show-B (SAME
            // persona, DIFFERENT show — the F116.2 transition branch) inside the window: this producer's
            // SAME-PERSONA branch re-evaluates, fails to resolve the now-deleted incoming persona, and
            // calls ClearStale on the SAME (SignOn, null) slot the still-held sign-on occupies.
            time.Advance(TimeSpan.FromMinutes(6)); // 11:55 -> 12:01
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(queue.Peek(SpeechDeferralKind.SignOn));

            // Real wall-clock time now reaches the held estimate (12:05) — the sign-on still airs.
            time.Advance(TimeSpan.FromMinutes(5)); // 12:01 -> 12:06
            var afterHold = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(afterHold);
            Assert.Equal(SegmentKind.SignOn, afterHold.SegmentKind);
        }

        [Fact]
        public async Task TheSamePersonaBranchesReArmPreservesTheGateRatherThanBypassingIt()
        {
            // Round-3 review — sibling of ARearmDuringALiveHoldPreservesTheGateRatherThanBypassingIt
            // above, pinning the SAME-PERSONA/DIFFERENT-SHOW branch's OWN notBefore carry-forward (a
            // genuinely different call site from the general branch's own re-arm) rather than its
            // ClearStale twin — Beta stays resolvable throughout here, so the ELSE (Enqueue) arm fires
            // instead of ClearStale.
            var crossing = MakeTrack("crossing", TimeSpan.FromMinutes(3));
            var catalog = FakeMediaCatalog.WithPool([crossing]);
            var (orchestrator, _, time, queue) = BuildChain(catalog, SamePersonaDifferentShowSchedule()); // noon, 12:03

            // Unit 1: arms the Alpha->Beta ceremony for noon.
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(queue.Peek(SpeechDeferralKind.SignOff));

            // Unit 2: 20 minutes already queued ahead — crosses. The SignOff declines and airs; the
            // paired (Beta) SignOn is held, gated to the window bound: NotBefore = 11:55 + 10min = 12:05.
            await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: (int)TimeSpan.FromMinutes(20).TotalMilliseconds), CancellationToken.None);
            var held = queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(held);
            Assert.Equal(JustBeforeNoon + TimeSpan.FromMinutes(10), held.NotBefore);

            // Real wall-clock time now crosses noon — still short of the held estimate (12:05). The
            // resolver's own "current" flips to Beta/show-A, whose OWN next block is Beta/show-B (SAME
            // persona, resolvable, DIFFERENT show) inside the window: this producer's SAME-PERSONA
            // branch re-evaluates and re-arms a FRESH show-transition sign-on over the SAME slot.
            time.Advance(TimeSpan.FromMinutes(6)); // 11:55 -> 12:01
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var reArmed = queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(reArmed);

            // The CONTENT correctly refreshed to the new, real boundary (show-A -> show-B, due 12:03).
            Assert.Equal(JustBeforeNoon + TimeSpan.FromMinutes(8), reArmed.Due);

            // But the GATE survived the re-arm — the still-draining Alpha->Beta tail's own hold (12:05)
            // is not silently bypassed just because a different ceremony's content now occupies this
            // slot.
            Assert.Equal(JustBeforeNoon + TimeSpan.FromMinutes(10), reArmed.NotBefore);
        }
    }
}
