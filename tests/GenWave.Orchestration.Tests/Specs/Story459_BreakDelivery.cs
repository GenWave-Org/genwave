// STORY-459 — BreakDelivery orders the buffer and keeps the books (gh-#401 · SPEC F192 · PLAN T536)
//
// BDD specification — xUnit. AC1–AC6 drive Deliver with hand-built outcomes; AC7/AC8 the drop policies. AC9 splits: its buffer half
// goes through the Orchestrator with a token cancelled mid-render, its reservation half calls BreakDelivery.Abandon directly
// (RenderPlanAsync keeps no BreakOutcome, so there is nothing to read back through GetNextAsync — SPEC F192.4's record-only rule).
//
// Deliver itself needs no clock and no async seam (SPEC F192.1 — a pure function of one BreakPlan and
// its outcomes), so every AC1–AC8 fact builds a plan directly and calls it synchronously; only AC9's
// BuffersNothing fact drives the real Orchestrator (the one class RenderPlanAsync's cancellation check
// lives on, SPEC F192.5). Round-2 review finding F1: BuffersNothing fires ct DURING render, not before
// GetNextAsync is even entered — the ONLY placement that actually distinguishes the fixed
// (check-after-render) code from the bug (check-before-render, which a ReadySource slot's
// always-already-complete RenderedOutcome sails straight through regardless of ct).

using System.Reflection;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Orchestration.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBreakDelivery
{
    static readonly DateTimeOffset Start = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
    static readonly StationIdentity Identity = new("station-1", "GenWave", "voice-station");
    static readonly HashSet<SpeechDeferralKind> NoHolds = [];

    static BreakContext MakeContext(string? unitDjName = null) =>
        new(1, null, null, unitDjName, new CadenceConfig(), Identity, Start, null, NoHolds, TimeSpan.Zero);

    static SegmentRequest MakeRequest(SegmentKind kind) =>
        new(kind, Identity.Voice, Identity.Name, null, Start, Identity.Id);

    static MediaItem MakeItem(string id, int? durationMs = null) =>
        new(id, $"/media/{id}.mp3", $"Track {id}", new Loudness(-23.0, -1.0, true), DurationMs: durationMs);

    static PlannedSlot RenderSlot(
        int ordinal, SegmentKind kind, Reservation? reservation = null, DropPolicy? drop = null,
        bool observeDuration = false) =>
        new(ordinal, kind, new RenderSource(MakeRequest(kind)), reservation, drop ?? new SilentDrop(), observeDuration);

    static PlannedSlot ReadySlot(int ordinal, SegmentKind kind, MediaItem item, Reservation? reservation = null) =>
        new(ordinal, kind, new ReadySource(item), reservation, new SilentDrop(), ObserveDuration: false);

    static BreakPlan MakePlan(TimeSpan budget, params PlannedSlot[] slots) =>
        new(1, MakeContext(), budget, slots);

    static BreakPlan MakePlanWithDj(TimeSpan budget, string unitDjName, params PlannedSlot[] slots) =>
        new(1, MakeContext(unitDjName), budget, slots);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioRenderedFailedRendered
    {
        // Given: outcomes for slots 1-3 — slot 2 fails, slots 1 and 3 render

        /// <summary>AC1 — Items holds slot 1 then slot 3, in order; the dropped slot 2 contributes nothing.</summary>
        [Fact]
        public void ItemsHoldSlotOneThenSlotThree()
        {
            var slot1 = ReadySlot(1, SegmentKind.BackAnnounce, MakeItem("one"));
            var slot2 = RenderSlot(2, SegmentKind.BackAnnounce);
            var slot3 = ReadySlot(3, SegmentKind.BackAnnounce, MakeItem("three"));
            var plan = MakePlan(TimeSpan.FromSeconds(5), slot1, slot2, slot3);
            var outcomes = new SlotOutcome[]
            {
                new RenderedOutcome(MakeItem("one")), new FailedOutcome(), new RenderedOutcome(MakeItem("three")),
            };
            var delivery = new BreakDelivery(NullLogger<BreakDelivery>.Instance, NoOpStationEventSink.Instance, new RollingPatterDurationEstimator());

            var outcome = delivery.Deliver(plan, outcomes);

            Assert.Equal(["one", "three"], outcome.Items.Select(i => i.MediaId));
        }
    }

    public sealed class ScenarioRenderedStationIdAnnouncementAndAd
    {
        // Given: three rendered slots (StationId, Announcement, Ad), unit DJ Ada

        /// <summary>AC2 — every stamped kind carries the unit's DJ name.</summary>
        [Fact]
        public void StampsEveryItemWithTheDjName()
        {
            var stationIdSlot = RenderSlot(1, SegmentKind.StationId);
            var announcementSlot = RenderSlot(2, SegmentKind.Announcement, new Reservation(ReservationKind.Announcement, "1"));
            var adSlot = ReadySlot(3, SegmentKind.Ad, MakeItem("ad-1"), new Reservation(ReservationKind.AdSpot, "ad-1"));
            var plan = MakePlanWithDj(TimeSpan.FromSeconds(5), "Ada", stationIdSlot, announcementSlot, adSlot);
            var outcomes = new SlotOutcome[]
            {
                new RenderedOutcome(MakeItem("tts:stationid-1")),
                new RenderedOutcome(MakeItem("tts:announcement-2")),
                new RenderedOutcome(MakeItem("ad-1")),
            };
            var delivery = new BreakDelivery(NullLogger<BreakDelivery>.Instance, NoOpStationEventSink.Instance, new RollingPatterDurationEstimator());

            var outcome = delivery.Deliver(plan, outcomes);

            Assert.All(outcome.Items, item => Assert.Equal("Ada", item.DjName));
        }
    }

    public sealed class ScenarioARenderedAnnouncementWithIdSeven
    {
        // Given: an Announcement slot reserved as id 7, rendered to "tts:abc123"

        /// <summary>AC3 — AnnouncementMediaId.Wrap(7, …)</summary>
        [Fact]
        public void WrapsTheMediaId()
        {
            var slot = RenderSlot(1, SegmentKind.Announcement, new Reservation(ReservationKind.Announcement, "7"));
            var plan = MakePlan(TimeSpan.FromSeconds(5), slot);
            var delivery = new BreakDelivery(NullLogger<BreakDelivery>.Instance, NoOpStationEventSink.Instance, new RollingPatterDurationEstimator());

            var outcome = delivery.Deliver(plan, [new RenderedOutcome(MakeItem("tts:abc123"))]);

            Assert.Equal(AnnouncementMediaId.Wrap(7, "tts:abc123"), Assert.Single(outcome.Items).MediaId);
        }
    }

    public sealed class ScenarioTwoRenderedSlotsWithDurations
    {
        // Given: ObserveDuration set on two Render slots, distinct measured durations

        /// <summary>AC4 — both observations reach the estimator, in ordinal order.</summary>
        [Fact]
        public void ObservesBothInOrder()
        {
            var slot1 = RenderSlot(1, SegmentKind.StationId, observeDuration: true);
            var slot2 = RenderSlot(2, SegmentKind.TimeDate, observeDuration: true);
            var plan = MakePlan(TimeSpan.FromSeconds(5), slot1, slot2);
            var outcomes = new SlotOutcome[]
            {
                new RenderedOutcome(MakeItem("tts:one", durationMs: 1000)),
                new RenderedOutcome(MakeItem("tts:two", durationMs: 2000)),
            };
            var estimator = new RecordingPatterDurationEstimator();
            var delivery = new BreakDelivery(NullLogger<BreakDelivery>.Instance, NoOpStationEventSink.Instance, estimator);

            delivery.Deliver(plan, outcomes);

            Assert.Equal(
                [(SegmentKind.StationId, TimeSpan.FromSeconds(1)), (SegmentKind.TimeDate, TimeSpan.FromSeconds(2))],
                estimator.Calls.Select(c => (c.Kind, c.Measured)));
        }
    }

    public sealed class ScenarioARenderedAdAndAFailedAnnouncement
    {
        // Given: reservations on both — the ad renders, the announcement fails

        static readonly Reservation AdReservation = new(ReservationKind.AdSpot, "ad-1");
        static readonly Reservation AnnouncementReservation = new(ReservationKind.Announcement, "9");

        static BreakOutcome Deliver()
        {
            var adSlot = ReadySlot(1, SegmentKind.Ad, MakeItem("ad-1"), AdReservation);
            var announcementSlot = RenderSlot(
                2, SegmentKind.Announcement, AnnouncementReservation, new WarnDrop(new AnnouncementWarnSubject()));
            var plan = MakePlan(TimeSpan.FromSeconds(5), adSlot, announcementSlot);
            var outcomes = new SlotOutcome[] { new RenderedOutcome(MakeItem("ad-1")), new FailedOutcome() };
            var delivery = new BreakDelivery(NullLogger<BreakDelivery>.Instance, NoOpStationEventSink.Instance, new RollingPatterDurationEstimator());
            return delivery.Deliver(plan, outcomes);
        }

        /// <summary>AC5 — the aired ad's reservation settles Aired.</summary>
        [Fact]
        public void MarksTheAdAired() =>
            Assert.Equal(new AiredReservation(), Deliver().Reservations[AdReservation]);

        /// <summary>AC5 — the dropped announcement's reservation settles Dropped with its cause.</summary>
        [Fact]
        public void MarksTheAnnouncementDropped() =>
            Assert.Equal(new DroppedReservation("render returned null"), Deliver().Reservations[AnnouncementReservation]);
    }

    public sealed class ScenarioTheDeliveryTypeReflected
    {
        // Given: typeof(BreakDelivery) fields

        /// <summary>AC6 — the buffer stays on the Orchestrator: no field is a closed Queue&lt;&gt;.</summary>
        [Fact]
        public void HoldsNoQueue()
        {
            var fields = typeof(BreakDelivery).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.DoesNotContain(fields, f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(Queue<>));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — segregated
    // ---------------------------------------------------------------------

    public sealed class ScenarioAFailedContextWithPolicyWarn
    {
        // Given: a ContextSegment slot policy-marked WarnDrop(ContextProviderWarnSubject), a failed render

        /// <summary>AC7 — exactly one WARN naming the provider.</summary>
        [Fact]
        public void LogsOneWarnNamingTheProvider()
        {
            var slot = RenderSlot(1, SegmentKind.ContextSegment, drop: new WarnDrop(new ContextProviderWarnSubject("weather")));
            var plan = MakePlan(TimeSpan.FromSeconds(5), slot);
            var logger = new CapturingLogger<BreakDelivery>();
            var delivery = new BreakDelivery(logger, NoOpStationEventSink.Instance, new RollingPatterDurationEstimator());

            delivery.Deliver(plan, [new FailedOutcome()]);

            var warning = Assert.Single(logger.Warnings);
            Assert.Contains("weather", warning, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioAFailedSignOffWithPolicyWarnAndEvent
    {
        // Given: a SignOff slot policy-marked WarnAndEventDrop, a failed render

        /// <summary>AC8 — exactly one HandoffPieceDropped event publishes.</summary>
        [Fact]
        public void PublishesOneHandoffPieceDropped()
        {
            var slot = RenderSlot(1, SegmentKind.SignOff, drop: new WarnAndEventDrop());
            var plan = MakePlan(TimeSpan.FromSeconds(5), slot);
            var events = new CapturingStationEventSink();
            var delivery = new BreakDelivery(NullLogger<BreakDelivery>.Instance, events, new RollingPatterDurationEstimator());

            delivery.Deliver(plan, [new FailedOutcome()]);

            var dropped = Assert.IsType<HandoffPieceDropped>(Assert.Single(events.Events));
            Assert.Equal("SignOff", dropped.Kind);
        }
    }

    public sealed class ScenarioACancelledTokenBeforeDelivery
    {
        // Given: served through the Orchestrator, a StationId deferral (RenderSource, a genuine TTS
        // render in flight) and a cadence-triggered ad (ReadySource — SPEC F191.2, already a
        // RenderedOutcome the instant BreakRenderer.KickSlot fires it) both due the SAME boundary, and
        // ct firing while that StationId render is still suspended — not before GetNextAsync is even
        // entered. The ad is the round-2 review finding F1 discriminating case: BreakRenderer never
        // throws on cancellation, so the OLD (before-render) check placement let this exact
        // already-complete ReadySource survive into the buffer no matter when ct fired; only a check
        // placed AFTER render (the fix) catches it.

        /// <summary>AC9 — no break item reaches the buffer; the boundary yields the plain rotation pick,
        /// not the ad slot that was already a RenderedOutcome when ct fired.</summary>
        [Fact]
        public async Task BuffersNothing()
        {
            var tts = new FakeTtsSegmentSource { RenderDelay = TimeSpan.FromSeconds(30) };
            var chain = new OrchestratorBuilder()
                .WithTts(tts)
                .WithAdCadence(new FakeAdCadenceProvider(1))
                .WithAdSpotVend(new FakeAdSpotVend { Answer = MakeItem("ad-1") })
                .Build();
            var ctx = new PlayoutContext([]);

            var firstTrack = await chain.Orchestrator.GetNextAsync(ctx, CancellationToken.None);

            chain.Queue.Enqueue(SpeechDeferralKind.StationId, "test: AC9 cancellation during render");

            using var cts = new CancellationTokenSource();
            var pullTask = chain.Orchestrator.GetNextAsync(ctx, cts.Token);

            // The StationId RenderSource slot's tts.RenderAsync call is kicked (BreakRenderer.KickSlot,
            // before RenderAsync's own first await) synchronously ahead of any suspension this test
            // thread could observe — RenderCallCount is already 1 by the time this loop is checked in
            // the common case; it only spins at all if some seam genuinely yielded first. No real delay
            // ever elapses: cts.Cancel() below fires long before FakeTtsSegmentSource's own 30s
            // RenderDelay could.
            while (tts.RenderCallCount == 0)
                await Task.Yield();

            cts.Cancel();

            var afterCancelledBoundary = await pullTask;

            Assert.Equal(firstTrack?.MediaId, afterCancelledBoundary?.MediaId);
        }

        /// <summary>AC9 — an abandoned unit carries no items at all: proof the whole plan was discarded
        /// rather than partially delivered. The per-reservation halves are the two facts below.</summary>
        [Fact]
        public void AbandonsEveryReservation()
        {
            var adReservation = new Reservation(ReservationKind.AdSpot, "ad-1");
            var announcementReservation = new Reservation(ReservationKind.Announcement, "7");
            var adSlot = ReadySlot(1, SegmentKind.Ad, MakeItem("ad-1"), adReservation);
            var announcementSlot = RenderSlot(2, SegmentKind.Announcement, announcementReservation);
            var plan = MakePlan(TimeSpan.FromSeconds(5), adSlot, announcementSlot);
            var delivery = new BreakDelivery(NullLogger<BreakDelivery>.Instance, NoOpStationEventSink.Instance, new RollingPatterDurationEstimator());

            var outcome = delivery.Abandon(plan);

            Assert.Empty(outcome.Items);
        }

        /// <summary>AC9 — the ad slot's own reservation specifically abandons (not merely "some" reservation).</summary>
        [Fact]
        public void AbandonsTheAdReservation()
        {
            var adReservation = new Reservation(ReservationKind.AdSpot, "ad-1");
            var adSlot = ReadySlot(1, SegmentKind.Ad, MakeItem("ad-1"), adReservation);
            var plan = MakePlan(TimeSpan.FromSeconds(5), adSlot);
            var delivery = new BreakDelivery(NullLogger<BreakDelivery>.Instance, NoOpStationEventSink.Instance, new RollingPatterDurationEstimator());

            var outcome = delivery.Abandon(plan);

            Assert.Equal(new AbandonedReservation(), outcome.Reservations[adReservation]);
        }

        /// <summary>AC9 — the announcement slot's own reservation specifically abandons too.</summary>
        [Fact]
        public void AbandonsTheAnnouncementReservation()
        {
            var announcementReservation = new Reservation(ReservationKind.Announcement, "7");
            var announcementSlot = RenderSlot(1, SegmentKind.Announcement, announcementReservation);
            var plan = MakePlan(TimeSpan.FromSeconds(5), announcementSlot);
            var delivery = new BreakDelivery(NullLogger<BreakDelivery>.Instance, NoOpStationEventSink.Instance, new RollingPatterDurationEstimator());

            var outcome = delivery.Abandon(plan);

            Assert.Equal(new AbandonedReservation(), outcome.Reservations[announcementReservation]);
        }
    }
}

// Records every ObserveRendered call (Kind, PersonaName, Voice, Measured), in call order — AC4 needs the
// ORDER two observations reach the estimator in, which CapturingPatterDurationEstimator cannot answer
// (its own ObserveRendered is a no-op, gh-#463 — it only ever records Estimate calls).
file sealed class RecordingPatterDurationEstimator : IPatterDurationEstimator
{
    public List<(SegmentKind Kind, string? PersonaName, string Voice, TimeSpan Measured)> Calls { get; } = [];

    public PatterDurationEstimate Estimate(SegmentKind kind, string? personaName, string voice) =>
        new(TimeSpan.FromSeconds(5), PatterEstimateConfidence.Heuristic);

    public void ObserveRendered(SegmentKind kind, string? personaName, string voice, TimeSpan measured) =>
        Calls.Add((kind, personaName, voice, measured));
}
