// STORY-320 — The boundary respects the backlog (gh-#469 · SPEC F124.1-.3, PLAN T266-T268)
//
// T266's own slice: the ladder's classification widens to treat a queue crossing the boundary as a
// straddle (SPEC F124.1) — a QueuedAhead that alone spans UntilBoundary settles Straddle, never
// CeremonyOnly, regardless of how little (or negative) room a NEW candidate would otherwise leave.
//
// T267's slice (amended at T266's review — no task owned the wiring until now): BOTH halves of
// F124.1's "sign-on is held" for a queue-crossing HANDOFF fit. The review's candidate (ii) shipped —
// see Orchestrator.TryServeCeremonyOnlyUnitAsync's own remarks for the full ruling and why candidate
// (i) (yielding the decline into straddle assembly) was rejected. In short: ShouldDeclineFinalUnit's
// own floor-only condition never needed to widen — crossing always forces below-floor too for a
// handoff kind, so every queue-crossing SignOff/SignOn fit was already landing on the decline path as
// of gh-#320. What changed is entirely inside TryServeCeremonyOnlyUnitAsync: it now calls
// BoundaryFitPlan.ClassifyOffToleranceRung directly (the SAME classifier below, moved off
// MusicSelectionPolicy at round-1 review finding F4) rather than hard-coding CeremonyOnly, and holds
// the paired SignOn (SPEC F124.1) instead of draining it alongside the SignOff in the same call.
// F124.3's drain-instant fix (T268 in the plan) folded into this same task — see the
// ScenarioTheCeremonyDrainInstantCountsTheQueue facts below, activated here.
//
// Round-1 review findings F1/F2 (gh-#469 verbatim, reproduced live): the hold's own mechanism —
// re-stamping the held SignOn's Due to max(Due, now + queuedAhead) — lasted exactly zero seconds
// (F1), and once it DID survive to the boundary, EnqueueHandoffCeremonyAsync's own window-exit clear
// destroyed it outright (F2). The fix moves the hold into SpeechDeferralQueue itself as a
// SpeechDeferral.NotBefore gate, checked against REAL wall-clock time regardless of any caller's own
// forced-forward "as of" instant — Due is left untouched (it keeps meaning "the boundary this
// deferral belongs to"), and SpeechDeferralQueue.ClearStale replaces the blind Clear inside
// Orchestrator's own ClearCeremony local so a still-held (NotBefore in the future) SignOn reads as
// LIVE, not stale. ScenarioTheHoldSurvivesRepeatedPullsAtTheSameInstant below reproduces F1 at the
// queue level (repeated pulls, one instant, one queuedAhead); Story303_StraddleHandoff's own
// AHeldSignOnSurvivesTheBoundaryAndAirsAfterTheQueuedTailDrains reproduces F2 on the real
// CachingScheduleResolver chain.
//
// Both scenarios in the ORIGINAL T266 slice below still arm a StationId deferral rather than a
// SignOff/SignOn — StationId is never declined by ShouldDeclineFinalUnit (handoff kinds only), so
// every fit there reaches MusicSelectionPolicy.SelectMusicCandidateAsync unconditionally, exercising
// the classification widening in isolation from the decline/hold-set region below. The NEW facts
// below arm real SignOff/SignOn handoffs to exercise that region directly.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBoundaryRespectsBacklog
{
    static readonly DateTimeOffset ClockStart = DateTimeOffset.Parse("2030-01-01T00:00:00Z");

    static readonly HandoffContext Handoff = new("af_flip", "Flip", "Mic Cardioid");

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
    /// the SPEC F111.5 rung token on the boundary-fit line can be asserted on directly. <paramref name="tts"/>
    /// defaults to a fresh double (every pre-T267 caller); the hold-path facts below pass their own so
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
        /// <summary>
        /// Round-1 review findings F1/F2 revised this scenario's own SHAPE: the hold now arms
        /// <see cref="SpeechDeferral.NotBefore"/> (a gate <see cref="SpeechDeferralQueue.TryDequeueDue"/>
        /// checks against REAL wall-clock time, regardless of any forced-forward "as of" instant a
        /// caller supplies) rather than re-stamping <see cref="SpeechDeferral.Due"/> — the re-stamp
        /// approach left the hold lasting exactly zero seconds, since a SignOn-headed fit's own
        /// UntilBoundary IS <c>Due - now</c>, so the very next pull's own forced drain instant already
        /// reached the re-stamped Due. <see cref="SpeechDeferral.Due"/> keeps meaning "the boundary this
        /// deferral belongs to" — untouched by the hold.
        /// </summary>
        static async Task<SpeechDeferral?> RunAsync(TimeSpan? preSeededNotBefore = null)
        {
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(SpeechDeferralKind.SignOff, "test: handoff armed", clock.GetUtcNow() + TimeSpan.FromSeconds(30), Handoff);
            queue.Enqueue(
                SpeechDeferralKind.SignOn, "test: handoff armed", clock.GetUtcNow() + TimeSpan.FromSeconds(45), Handoff,
                notBefore: preSeededNotBefore is { } n ? clock.GetUtcNow() + n : null);

            var catalog = FakeMediaCatalog.WithPool([]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            await orchestrator.GetNextAsync(new PlayoutContext([], QueuedAheadMs: 200_000), CancellationToken.None);

            return queue.Peek(SpeechDeferralKind.SignOn);
        }

        [Fact]
        public static async Task The_held_SignOns_NotBefore_is_armed_to_now_plus_queuedAhead()
        {
            // SPEC F124.1/F124.2 — a SignOff due in 30s (boundary 45s out, given SignOffLeadTime) with
            // 200s already queued ahead crosses (SPEC F124.1), so the paired SignOn (originally due at
            // the boundary itself, 45s) is held and its NotBefore armed to now + queuedAhead (200s):
            // the SAME gate every LATER pull's own TryDequeueDue call re-checks against the real clock,
            // regardless of how far forward that later pull's own forced "as of" instant reaches.
            var heldSignOn = await RunAsync();

            Assert.NotNull(heldSignOn);
            Assert.Equal(ClockStart + TimeSpan.FromSeconds(200), heldSignOn.NotBefore);
        }

        [Fact]
        public static async Task The_held_SignOns_Due_stays_the_original_boundary()
        {
            // Due keeps meaning "the boundary this deferral belongs to" (round-1 review) — the hold
            // never touches it, only NotBefore, so EnqueueHandoffCeremonyAsync's own reconcile/window
            // logic keeps reading a truthful boundary for a held SignOn.
            var heldSignOn = await RunAsync();

            Assert.NotNull(heldSignOn);
            Assert.Equal(ClockStart + TimeSpan.FromSeconds(45), heldSignOn.Due);
        }

        [Fact]
        public static async Task A_NotBefore_already_past_the_estimate_is_not_moved_backward()
        {
            // max() semantics: a SignOn manually seeded with a NotBefore already far out (500s) is NOT
            // pulled backward to now + queuedAhead (200s) just because the SignOff crosses — queuedAhead
            // is an honest FLOOR (SPEC F124.2), never authoritative over a gate that already sits later
            // than it.
            var heldSignOn = await RunAsync(preSeededNotBefore: TimeSpan.FromSeconds(500));

            Assert.NotNull(heldSignOn);
            Assert.Equal(ClockStart + TimeSpan.FromSeconds(500), heldSignOn.NotBefore);
        }
    }

    public static class ScenarioAQueueCrossingSignOffReachesTheHoldPath
    {
        [Fact]
        public static async Task The_SignOn_does_not_render_in_the_same_unit_as_a_queue_crossing_SignOff()
        {
            // The review's MANDATORY mutation-resistant fact: authored so that reverting the
            // QueuedTailCrossesBoundary consultation BoundaryFitPlan.ClassifyOffToleranceRung makes
            // (the SAME classifier Orchestrator.TryServeCeremonyOnlyUnitAsync now calls directly, PLAN
            // T267 — the "CrossesBoundary union" the review named) turns this fact red: without it, a
            // below-floor SignOff fit's rung falls back to the pre-F124 CeremonyOnly literal, the hold
            // condition below never fires, and the SignOn renders alongside the SignOff in this same
            // call (the pre-F124 inversion). This is mutation-green after T266 alone (T266 never wired
            // anything to consult its own widened classifier from the decline path) — only T267's own
            // wiring makes it mutation-resistant.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(SpeechDeferralKind.SignOff, "test: handoff armed", clock.GetUtcNow() + TimeSpan.FromSeconds(30), Handoff);
            queue.Enqueue(SpeechDeferralKind.SignOn, "test: handoff armed", clock.GetUtcNow() + TimeSpan.FromSeconds(45), Handoff);

            var catalog = FakeMediaCatalog.WithPool([]);
            var tts = new FakeTtsSegmentSource();
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger, tts);

            await orchestrator.GetNextAsync(new PlayoutContext([], QueuedAheadMs: 200_000), CancellationToken.None);

            Assert.Contains(tts.Requests, r => r.Kind == SegmentKind.SignOff);
            Assert.DoesNotContain(tts.Requests, r => r.Kind == SegmentKind.SignOn);
        }
    }

    public static class ScenarioTheHoldSurvivesRepeatedPullsAtTheSameInstant
    {
        [Fact]
        public static async Task The_SignOn_never_appears_in_a_chain_pulled_at_one_instant_with_one_queuedAhead()
        {
            // Round-1 review finding F1's own reproduction, in CHAIN form: gh-#469's numbers, replayed
            // by pulling repeatedly at ONE clock instant with ONE unchanging queuedAhead, exactly as the
            // production feeder does between two genuine boundary decisions (planning runs far more
            // often than real audio actually drains). Round-1's own shape RED here: pull0 the SignOff,
            // pull1 the "held" SignOn (re-stamped Due already satisfied by that SAME pull's own forced
            // drain instant — the hold lasting zero seconds), pull2 music — gh-#469 verbatim. The fix
            // (NotBefore, gated against REAL wall-clock time regardless of any pull's own forced "as of")
            // keeps the SignOn out of every chain pulled at this one instant, however many pulls it takes
            // to reach a real track.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(SpeechDeferralKind.SignOff, "test: handoff armed", clock.GetUtcNow() + TimeSpan.FromSeconds(30), Handoff);
            queue.Enqueue(SpeechDeferralKind.SignOn, "test: handoff armed", clock.GetUtcNow() + TimeSpan.FromSeconds(45), Handoff);

            var catalog = FakeMediaCatalog.WithPool([MakeTrack("full-length", TimeSpan.FromMinutes(3.5))]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            var ctx = new PlayoutContext([], QueuedAheadMs: 200_000);
            var chain = new List<MediaItem>();
            for (var pull = 0; pull < 10; pull++)
            {
                var next = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
                Assert.NotNull(next);
                chain.Add(next);
                if (!next.MediaId.StartsWith("tts:", StringComparison.Ordinal)) break; // reached a real track
            }

            Assert.DoesNotContain(chain, item => item.SegmentKind == SegmentKind.SignOn);
        }
    }

    public static class ScenarioTheCeremonyDrainInstantCountsTheQueue
    {
        [Fact]
        public static async Task The_drain_instant_includes_QueuedAhead_not_UntilBoundary_alone()
        {
            // SPEC F124.3 (folded into T267): the ceremony-only unit's drain instant is
            // Max(UntilBoundary, QueuedAhead), not UntilBoundary alone. Proof: a StationId cadence
            // deferral due at T+100s — strictly BETWEEN the boundary (T+45s: SignOff due T+30s plus the
            // 15s lead time) and the queued-tail estimate (T+200s) — rides the SAME ceremony-only unit
            // as the sign-off. The pre-F124.3 drain instant (T+45s alone) would have left it pending for
            // a later boundary; this one reaches it because the drain now runs as-of T+200s.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(SpeechDeferralKind.SignOff, "test: handoff armed", clock.GetUtcNow() + TimeSpan.FromSeconds(30), Handoff);
            queue.Enqueue(SpeechDeferralKind.StationId, "test: cadence armed", clock.GetUtcNow() + TimeSpan.FromSeconds(100));

            var catalog = FakeMediaCatalog.WithPool([]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            var first = await orchestrator.GetNextAsync(new PlayoutContext([], QueuedAheadMs: 200_000), CancellationToken.None);
            var second = await orchestrator.GetNextAsync(new PlayoutContext([], QueuedAheadMs: 200_000), CancellationToken.None);

            Assert.NotNull(first);
            Assert.Equal(SegmentKind.SignOff, first.SegmentKind);
            Assert.NotNull(second);
            Assert.Equal(SegmentKind.StationId, second.SegmentKind);
        }
    }

    public static class ScenarioTheSignOffStillLeadsTheTail
    {
        [Fact]
        public static async Task The_SignOff_drains_at_the_next_seam_ahead_of_the_queued_content()
        {
            // The existing straddle sound, unchanged: the outgoing DJ's goodbye precedes their own
            // buffered tail; only the SIGN-ON waits for the drain (SPEC F124.1).
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(SpeechDeferralKind.SignOff, "test: handoff armed", clock.GetUtcNow() + TimeSpan.FromSeconds(30), Handoff);
            queue.Enqueue(SpeechDeferralKind.SignOn, "test: handoff armed", clock.GetUtcNow() + TimeSpan.FromSeconds(45), Handoff);

            var catalog = FakeMediaCatalog.WithPool([]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            var next = await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: 200_000), CancellationToken.None);

            Assert.NotNull(next);
            Assert.Equal(SegmentKind.SignOff, next.SegmentKind);
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

        [Fact]
        public static async Task A_null_QueuedAhead_leaves_the_drain_instant_unchanged()
        {
            // SPEC F124.3's own null-degrade (folded into T267): no QueuedAheadMs at all coalesces to
            // zero, which can never exceed a strictly-positive UntilBoundary — Max(UntilBoundary,
            // QueuedAhead) always resolves to UntilBoundary alone, so the drain instant (and therefore
            // which pieces drain together) is byte-identical to pre-F124: both ceremony pieces still
            // drain in the SAME call, exactly as before this epic.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(SpeechDeferralKind.SignOff, "test: handoff armed", clock.GetUtcNow() + TimeSpan.FromSeconds(30), Handoff);
            queue.Enqueue(SpeechDeferralKind.SignOn, "test: handoff armed", clock.GetUtcNow() + TimeSpan.FromSeconds(45), Handoff);

            var catalog = FakeMediaCatalog.WithPool([]);
            var tts = new FakeTtsSegmentSource();
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger, tts);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Contains(tts.Requests, r => r.Kind == SegmentKind.SignOff);
            Assert.Contains(tts.Requests, r => r.Kind == SegmentKind.SignOn);
            Assert.Contains(
                logger.Entries, entry => entry.Message.Contains("rung=CeremonyOnly", StringComparison.Ordinal));
        }
    }

    // ── ROUND-2 REVIEW — the peek-blindness fix's own coverage ────────────────

    public sealed class ScenarioAStaleHeldSignOnNeverBlindsTheFitToOtherKinds
    {
        [Fact]
        public async Task A_StationId_due_soon_still_builds_a_boundary_fit_and_drains_on_time()
        {
            // SPEC F124.1/F124.2 round-2 review finding F2 (Major) — once real wall-clock time passes a
            // HELD SignOn's own Due (stale, but still gated by NotBefore), the OLD blind PeekNextDue
            // kept reporting it as "next up" regardless: untilDue (Due - now) went NEGATIVE, so
            // GetNextAsync's own "untilDue > TimeSpan.Zero" guard refused to build ANY fit at all — for
            // ANY kind — for the whole remainder of the hold, since the held entry kept winning the
            // earliest-due comparison against every other, perfectly eligible pending deferral. F74.3
            // bias, a gh-#300 decline, a fresh boundary's own SignOff — all dark. A StationId due soon
            // is completely unrelated to the held SignOn (only a SignOn ever carries NotBefore) — with
            // the fix, PeekNextDue skips the gated entry, the StationId heads the fit instead, and the
            // whole boundary-fit machinery stays live throughout the hold.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);

            // A stale-but-held SignOn: Due already elapsed (as if real time had already crossed the
            // boundary it was armed for), NotBefore still gating it 5 minutes further out — exactly the
            // shape a queue-crossing decline's own hold leaves behind once real time outruns Due.
            queue.Enqueue(
                SpeechDeferralKind.SignOn, "test: stale but held",
                due: clock.GetUtcNow() - TimeSpan.FromSeconds(5), handoff: Handoff,
                notBefore: clock.GetUtcNow() + TimeSpan.FromMinutes(5));

            // A StationId due soon, well within the window — unrelated to the held SignOn, never gated.
            queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 100s", clock.GetUtcNow() + TimeSpan.FromSeconds(100));

            var pool = MakeTrack("still-plays", TimeSpan.FromMinutes(3));
            var catalog = FakeMediaCatalog.WithPool([pool]);
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // The fit machinery ran for the StationId — the SAME "Boundary fit (...)" line every
            // in-window deferral gets, never suppressed just because a stale-but-held SignOn also
            // happens to be pending.
            Assert.Contains(
                logger.Entries,
                entry => entry.Message.Contains("Boundary fit (StationId)", StringComparison.Ordinal));

            // The ident itself drains on time — not stalled behind the held SignOn either.
            clock.Advance(TimeSpan.FromSeconds(100));
            var next = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(next);
            Assert.Equal(SegmentKind.StationId, next.SegmentKind);
        }
    }

    public sealed class ScenarioTheDrainInstantsTwoTernaryTermsArePinned
    {
        // Round-2 review finding F7 — two branch terms inside TryServeCeremonyOnlyUnitAsync's own
        // drain-instant arithmetic that were green even if removed/mutated, with no existing fact
        // distinguishing their true/false outcome. Both are genuinely load-bearing (documented,
        // intentional SPEC behavior — see that method's own remarks), so both are PINNED here rather
        // than deleted.

        [Fact]
        public async Task ASignOnHeadedCrossingNeverChasesQueuedAheadForSiblingDeferrals()
        {
            // Pins the "fit.Kind == SpeechDeferralKind.SignOff" term of the drain-instant ternary
            // (`fit.Kind == SpeechDeferralKind.SignOff && fit.QueuedTailCrossesBoundary ? chasedQueuedAhead
            // : fit.UntilBoundary`). A one-sided SignOn-only ceremony (SPEC F92.3's "into music-only"
            // shape — no SignOff paired) that ALSO crosses the boundary reaches this SAME ternary with
            // Kind == SignOn, not SignOff — the term must keep the drain pinned to fit.UntilBoundary
            // (never chasing the full queued tail) exactly as SPEC F124.1's own remarks require ("a
            // SignOn-headed fit... does NOT chase QueuedAhead at all"). Removing/inverting the Kind
            // check would let this SAME crossing SignOn's own decline chase the full 200s queued tail
            // instead, sweeping a sibling deferral (TimeDate, due at 100s — strictly between the 45s
            // boundary and the 200s queued tail) into the SAME drain call it has nothing to do with.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.SignOn, "test: one-sided transition", clock.GetUtcNow() + TimeSpan.FromSeconds(45), Handoff);
            queue.Enqueue(
                SpeechDeferralKind.TimeDate, "test: due between the boundary and the queued tail",
                clock.GetUtcNow() + TimeSpan.FromSeconds(100));

            var catalog = FakeMediaCatalog.WithPool([]);
            var tts = new FakeTtsSegmentSource();
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger, tts);

            await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: 200_000), CancellationToken.None);

            // The SignOn itself still drains, ordinarily, at its own UntilBoundary (45s) — sanity check
            // this really is the crossing-decline path.
            Assert.Contains(tts.Requests, r => r.Kind == SegmentKind.SignOn);

            // The unrelated TimeDate deferral (due at 100s) was NOT swept into this same call — proof
            // the drain instant stayed at UntilBoundary (45s), never chasing the full 200s queued tail
            // the way a SignOff-headed crossing legitimately does.
            Assert.NotNull(queue.Peek(SpeechDeferralKind.TimeDate));
        }

        [Fact]
        public async Task ASignOffHeadedCrossingClampsTheChaseToTheF74Window()
        {
            // Pins the "fit.QueuedAhead < boundaryBiasProvider.Current" clamp term of the SAME
            // ternary's own `chasedQueuedAhead` local. A SignOff-headed crossing with a queued tail
            // LARGER than the F74.3 lookahead window (900s queued, 600s window) must clamp its own
            // drain instant to the window bound (600s), never the full 900s — otherwise a sibling
            // deferral due strictly BETWEEN the window and the full queued tail (TimeDate at 700s) gets
            // swept into this SAME call, chasing well past the very window every fit in this class is
            // built inside of.
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.SignOff, "test: handoff armed", clock.GetUtcNow() + TimeSpan.FromSeconds(30), Handoff);
            queue.Enqueue(
                SpeechDeferralKind.TimeDate, "test: due beyond the window but inside the queued tail",
                clock.GetUtcNow() + TimeSpan.FromMinutes(11) + TimeSpan.FromSeconds(40));

            var catalog = FakeMediaCatalog.WithPool([]);
            var tts = new FakeTtsSegmentSource();
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(catalog, queue, clock, logger, tts);

            await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: 900_000), CancellationToken.None);

            // The SignOff itself still drains, ordinarily — sanity check this really is the
            // crossing-decline path.
            Assert.Contains(tts.Requests, r => r.Kind == SegmentKind.SignOff);

            // The TimeDate deferral (due beyond the window, but still inside the raw 900s queued tail)
            // stayed pending — proof the drain instant clamped to the 600s window, never chased the full
            // unclamped queued tail.
            Assert.NotNull(queue.Peek(SpeechDeferralKind.TimeDate));
        }
    }
}
