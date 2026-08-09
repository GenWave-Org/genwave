// STORY-303 — The straddle handoff (F111, gh-#320, closes gh-#300)

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureStraddleHandoff
{
    static readonly DateTimeOffset ClockStart = DateTimeOffset.Parse("2030-01-01T00:00:00Z");

    /// <summary>Mirrors Gh254_BoundaryFitSelection/Gh300_DeclineTheFinalUnit's own track builder verbatim.</summary>
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

    /// <summary>Drives the real Orchestrator.GetNextAsync -> MusicSelectionPolicy.SelectMusicCandidateAsync
    /// seam through fakes — the same idiom Gh254/Gh300 established — with a CapturingLogger so the
    /// SPEC F111.5 rung token on the boundary-fit line can be asserted on directly.</summary>
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

        [Fact]
        public async Task BelowTheFloorDeclinePathHardCodesCeremonyOnly()
        {
            // T234 review finding F2: this fact pins the DECLINE path's own hard-coded CeremonyOnly
            // literal (TryServeCeremonyOnlyUnitAsync's LogBoundaryFit call) — ShouldDeclineFinalUnit
            // fires (deeply negative room: 200s already queued ahead of a boundary 45s out), the
            // ceremony airs, and the log line never reaches MusicSelectionPolicy.ClassifyOffToleranceRung
            // at all. It does NOT exercise the policy's own classifier arm — see
            // BelowFloorNotDeclinedStillClassifiesCeremonyOnlyViaPolicy for that coverage (F2 found the
            // classifier arm had none: mutating it to always return Straddle left this fact green).
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
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            var next = await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: 200_000), CancellationToken.None);

            // The ceremony airs instead of music — no candidate was even sampled.
            Assert.NotNull(next);
            Assert.StartsWith("tts:", next.MediaId, StringComparison.Ordinal);
            Assert.Contains(
                logger.Entries, entry => entry.Message.Contains("rung=CeremonyOnly", StringComparison.Ordinal));
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
            // (MusicSelectionPolicy.IsBelowFloor, called from ClassifyOffToleranceRung AND
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
        [Fact(Skip = "Pending T235 — see docs/PLAN.md")]
        public void TheStraddleUnitAirsSignOffThenTheCrossingTrack()
        {
            // Straddle outcome ⇒ this unit's buffer is [SignOff piece, crossing track];
            // SignOn is NOT in it.
            // Assert.Equal(new[] { SegmentKind.SignOff, null }, bufferedKinds);
            Assert.Fail("pending T235");
        }

        [Fact(Skip = "Pending T235 — see docs/PLAN.md")]
        public void SignOnDrainsAtTheSeamAfterTheCrossingTrack()
        {
            // The hold-set keeps SignOn queued through the straddle seam; the NEXT
            // GetNextAsync drains it first.
            // Assert.Equal(SegmentKind.SignOn, nextUnitFirstItem.SegmentKind);
            Assert.Fail("pending T235");
        }

        [Fact(Skip = "Pending T235 — see docs/PLAN.md")]
        public void TheSignOnCopyCanNameTheCrossingTrack()
        {
            // The handoff context captured at plan time carries the crossing track's
            // title/artist; the copywriter's back-announce line receives them.
            // Assert.Contains(crossingTrack.Title, capturedPrompt);
            Assert.Fail("pending T235");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDegradePerPiece
    {
        [Fact(Skip = "Pending T235 — see docs/PLAN.md")]
        public void AFailedSignOffStillAirsTheCrossingTrackAndSignOn()
        {
            // F92.4: whichever piece rendered airs; music never waits; WARN + booth entry.
            // Assert.Contains(buffer, i => i.SegmentKind is null); // the track is there
            Assert.Fail("pending T235");
        }

        [Fact(Skip = "Pending T235 — see docs/PLAN.md")]
        public void NeverBackToBack()
        {
            // In no straddle outcome do SignOff and SignOn appear adjacent in one unit —
            // the exact gh-#300 field report shape is structurally impossible.
            // Assert.NotEqual(..adjacent SignOff/SignOn..);
            Assert.Fail("pending T235");
        }
    }
}
