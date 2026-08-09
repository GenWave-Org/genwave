// STORY-301 — Top-of-hour idents from the imaging pool (F110.1, F110.2, gh-#381)

using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureClockAnchoredIdents
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioClockAnchoredAndOptIn
    {
        [Fact]
        public void TheProducerEnqueuesAFutureDatedStationIdBeforeTheHour()
        {
            // ClockAnchoredImagingProducer with ClockAnchoredIdents=true and the station clock
            // approaching the hour ⇒ one future-dated StationId deferral, due at the top. Station
            // zone (America/Denver, UTC-6) is deliberately DIFFERENT from the underlying
            // TimeProvider's own UTC "now" — a real regression here (e.g. reading the container's
            // clock instead of the station's) would compute the wrong due instant, proving the fact
            // is station-local, not merely "whatever timezone the machine has" (mirrors
            // Gh224_StationZoneScheduleAndTasteClock's own posture).
            var denver = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
            var stationLocalNow = new DateTimeOffset(2026, 8, 8, 13, 45, 0, TimeSpan.FromHours(-6));
            var stationClock = new FakeStationClockProvider(stationLocalNow, denver);
            var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(timeProvider);
            var settings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(ClockAnchoredIdents: true, TimeAnnouncements: false),
            };
            var producer = new ClockAnchoredImagingProducer(queue, settings, timeProvider, stationClock);

            producer.Produce();

            var topOfHour = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.FromHours(-6));
            Assert.Equal(topOfHour, queue.NextDue);
        }

        [Fact]
        public void SupersedeKeepsExactlyOnePending()
        {
            // Two producer ticks before the same hour ⇒ one pending deferral (F74.2). No
            // IStationClockProvider here — exercises the TimeProvider fallback path instead, with
            // FakeTimeProvider.Advance simulating the wall clock moving between ticks.
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 13, 45, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var settings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(ClockAnchoredIdents: true, TimeAnnouncements: false),
            };
            var producer = new ClockAnchoredImagingProducer(queue, settings, clock);

            producer.Produce(); // first tick, ~15 minutes before the hour
            clock.Advance(TimeSpan.FromMinutes(10));
            producer.Produce(); // second tick, still before the same hour

            var topOfHour = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
            var due = queue.TryDequeueDue(topOfHour);
            var only = Assert.Single(due);
            Assert.Equal(SpeechDeferralKind.StationId, only.Kind);
        }

        [Fact]
        public void BothKnobsOnShareTheSameDueInstant()
        {
            // PLAN T230 review F4 — the class docs claim StationId and TimeDate enqueue at the SAME
            // station-local top-of-hour instant when both knobs are on (SPEC F110.1/F110.3); assert it
            // directly rather than trusting the doc.
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 13, 45, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var settings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(ClockAnchoredIdents: true, TimeAnnouncements: true),
            };
            var producer = new ClockAnchoredImagingProducer(queue, settings, clock);

            producer.Produce();

            var topOfHour = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
            var due = queue.TryDequeueDue(topOfHour);
            Assert.Equal(2, due.Count);
            Assert.All(due, deferral => Assert.Equal(topOfHour, deferral.Due));
            Assert.Contains(due, deferral => deferral.Kind == SpeechDeferralKind.StationId);
            Assert.Contains(due, deferral => deferral.Kind == SpeechDeferralKind.TimeDate);
        }

        [Fact(Skip = "Pending T232 — see docs/PLAN.md")]
        public void PoolFirstAiring()
        {
            // With a ready authored station_id row in the fake catalog, the drain airs the
            // authored MediaItem (no TTS render for the ident).
            // Assert.Equal(authoredItem.Id, bufferedItem.Id);
            Assert.Fail("pending T232");
        }

        [Fact(Skip = "Pending T232 — see docs/PLAN.md")]
        public void EmptyPoolFallsBackToTheTemplatedIdent()
        {
            // Empty pool ⇒ today's templated TTS ident renders unchanged (station voice,
            // never LLM-authored).
            // Assert.Equal(SegmentKind.StationId, capturedRequest.Kind);
            Assert.Fail("pending T232");
        }
    }

    public sealed class ScenarioDueButUnairedIdentSurvivesTheHourTurning
    {
        [Fact]
        public void APostHourTickNeverErasesTheStillPendingIdent()
        {
            // PLAN T230 review F1 — REAL DEFECT pin: a 30-day sim found 70-92% of hourly idents
            // never aired, because the producer's OWN next tick — landing after the hour turned but
            // BEFORE any track boundary drained the 14:00 deferral — used to overwrite it with a
            // future-dated 15:00 deferral (Enqueue's unconditional supersede). Exact pin: tick 13:45
            // arms 14:00; tick 14:00:15 (no drain in between — the boundary that would drain it
            // hasn't landed yet) must NOT erase it; TryDequeueDue(14:02) must still yield the 14:00
            // StationId.
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 13, 45, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var settings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(ClockAnchoredIdents: true, TimeAnnouncements: false),
            };
            var producer = new ClockAnchoredImagingProducer(queue, settings, clock);

            producer.Produce(); // tick 13:45 — arms the 14:00 deferral
            clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(15)); // 14:00:15 — hour turned, no drain
            producer.Produce(); // tick 14:00:15 — must not clobber the still-pending 14:00 deferral

            var due = queue.TryDequeueDue(new DateTimeOffset(2026, 8, 8, 14, 2, 0, TimeSpan.Zero));
            var only = Assert.Single(due);
            Assert.Equal(SpeechDeferralKind.StationId, only.Kind);
            Assert.Equal(new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero), only.Due);
        }

        [Fact]
        public void OnceDrainedTheNextTickArmsTheFollowingHourWithNoBacklog()
        {
            // Normal flow resumes the instant the pending deferral actually drains: the queue is
            // empty again, so the very next tick is free to arm the FOLLOWING hour — exactly one
            // pending deferral, never a backlog.
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 13, 45, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var settings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(ClockAnchoredIdents: true, TimeAnnouncements: false),
            };
            var producer = new ClockAnchoredImagingProducer(queue, settings, clock);

            producer.Produce(); // tick 13:45 — arms 14:00
            clock.Advance(TimeSpan.FromMinutes(17)); // 14:02 — a boundary decision drains it
            var drained = queue.TryDequeueDue(clock.GetUtcNow());
            Assert.Single(drained);

            producer.Produce(); // next tick — the slot is empty again, free to arm 15:00

            Assert.Equal(new DateTimeOffset(2026, 8, 8, 15, 0, 0, TimeSpan.Zero), queue.NextDue);
        }
    }

    public sealed class ScenarioDstAndBoundaryTicks
    {
        // PLAN T230 review F2/F3/F4 — the shared WallClockInstantResolver this producer now delegates
        // to (byte-identical to ScheduleResolver's own DST math) applied to the top-of-hour target.

        static TimeZoneInfo DenverZone => TimeZoneInfo.FindSystemTimeZoneById("America/Denver");

        [Fact]
        public void SpringForwardTargetHourStepsForwardToTheFirstValidMinute()
        {
            // Denver springs forward 02:00->03:00 on 2026-03-08 (the hour never happens). Now =
            // 01:45 MST — the target top-of-hour (02:00) lands inside the gap and must step FORWARD
            // to the first wall-clock minute that DOES exist (03:00 MDT).
            var stationLocalNow = new DateTimeOffset(2026, 3, 8, 1, 45, 0, TimeSpan.FromHours(-7));
            var stationClock = new FakeStationClockProvider(stationLocalNow, DenverZone);
            var timeProvider = new FakeTimeProvider(stationLocalNow);
            var queue = new SpeechDeferralQueue(timeProvider);
            var settings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(ClockAnchoredIdents: true, TimeAnnouncements: false),
            };
            var producer = new ClockAnchoredImagingProducer(queue, settings, timeProvider, stationClock);

            producer.Produce();

            Assert.Equal(new DateTimeOffset(2026, 3, 8, 3, 0, 0, TimeSpan.FromHours(-6)), queue.NextDue);
        }

        [Fact]
        public void FallBackTargetHourResolvesToItsFirstOccurrence()
        {
            // Denver falls back 02:00->01:00 on 2026-11-01 (the hour happens twice). Now = 00:45
            // MDT, before the repeat begins — the target top-of-hour (01:00) is ambiguous and must
            // resolve to its FIRST occurrence (still MDT, -06:00), the offset still in effect before
            // the clocks roll back.
            var stationLocalNow = new DateTimeOffset(2026, 11, 1, 0, 45, 0, TimeSpan.FromHours(-6));
            var stationClock = new FakeStationClockProvider(stationLocalNow, DenverZone);
            var timeProvider = new FakeTimeProvider(stationLocalNow);
            var queue = new SpeechDeferralQueue(timeProvider);
            var settings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(ClockAnchoredIdents: true, TimeAnnouncements: false),
            };
            var producer = new ClockAnchoredImagingProducer(queue, settings, timeProvider, stationClock);

            producer.Produce();

            Assert.Equal(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.FromHours(-6)), queue.NextDue);
        }

        [Fact]
        public void AntarcticaTrollsTwoHourFallBackNeverArmsAPastDueInstant()
        {
            // PLAN T230 review F3 — REAL DEFECT pin: Antarctica/Troll's fall-back is a 2-HOUR shift
            // (+02:00 -> +00:00 on 2026-10-25, not the usual 1-hour DST delta), so the ambiguous
            // wall-clock window is twice as wide — wide enough that the target hour's FIRST
            // occurrence can already be well in the past by the time a LATER tick lands during the
            // SECOND pass through the same repeated hour. Now = 01:30 local (second pass, offset
            // +00:00) — the target top-of-hour (02:00)'s FIRST occurrence is 00:00Z, already 90
            // minutes in the past; the producer's own prior hand-rolled DST copy (pre-F2/F3) always
            // picked the first occurrence and would have re-armed that past instant on every tick.
            var trollZone = TimeZoneInfo.FindSystemTimeZoneById("Antarctica/Troll");
            var stationLocalNow = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero); // second pass
            var stationClock = new FakeStationClockProvider(stationLocalNow, trollZone);
            var timeProvider = new FakeTimeProvider(stationLocalNow);
            var queue = new SpeechDeferralQueue(timeProvider);
            var settings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(ClockAnchoredIdents: true, TimeAnnouncements: false),
            };
            var producer = new ClockAnchoredImagingProducer(queue, settings, timeProvider, stationClock);

            producer.Produce();

            var topOfHour = queue.NextDue;
            Assert.True(topOfHour > stationLocalNow, "a resolved top-of-hour instant must never already be in the past");
            Assert.Equal(new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.Zero), topOfHour); // the second occurrence
        }

        [Fact]
        public void ATickLandingExactlyOnTheHourTargetsTheFollowingHour()
        {
            // A tick landing exactly ON a wall-clock hour boundary must target the FOLLOWING hour,
            // never the one that just started (pins NextStationLocalTopOfHour's own doc claim).
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var settings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(ClockAnchoredIdents: true, TimeAnnouncements: false),
            };
            var producer = new ClockAnchoredImagingProducer(queue, settings, clock);

            producer.Produce();

            Assert.Equal(new DateTimeOffset(2026, 8, 8, 15, 0, 0, TimeSpan.Zero), queue.NextDue);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDefaultsChangeNothing
    {
        [Fact]
        public void ClockAnchoringOffEnqueuesNothingEver()
        {
            // Default false ⇒ the producer never enqueues; StationIdEveryNUnits cadence
            // remains the only ident source — byte-identical sound.
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 13, 45, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var settings = new FakeStationImagingSettingsProvider(); // both-false default
            var producer = new ClockAnchoredImagingProducer(queue, settings, clock);

            producer.Produce();
            clock.Advance(TimeSpan.FromMinutes(20));
            producer.Produce(); // repeated ticks — still nothing, ever

            Assert.Null(queue.NextDue);
        }
    }
}
