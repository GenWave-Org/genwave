// STORY-301 — Top-of-hour idents from the imaging pool (F110.1, F110.2, gh-#381)

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureClockAnchoredIdents
{
    // PoolFirstAiring/EmptyPoolFallsBackToTheTemplatedIdent (PLAN T232) exercise the ORCHESTRATOR's
    // own drain arm, not the producer above them — mirrors Story297_ContextSegmentsAir.cs's harness
    // idiom (a real Orchestrator wired to fakes at the catalog/tts/clock seams).

    static MediaReference MakeTrackRef(string id) => new(
        id, $"/media/{id}.mp3", $"Track {id}", new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    static Orchestrator BuildOrchestrator(
        SpeechDeferralQueue queue, TimeProvider clock, FakeTtsSegmentSource tts, FakeMediaCatalog catalog,
        CadenceConfig? cadence = null)
    {
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(cadence ?? new CadenceConfig
        {
            LeadInBeforeEachTrack = false,
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits = 0,
        });
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        var musicSelectionPolicy = new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance);

        return new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, musicSelectionPolicy, tts,
            new FakeActivePersonaAccessor(), NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            queue, clock, new FakeBoundaryBiasProvider(TimeSpan.Zero),
            catalog: catalog);
    }

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

        [Fact]
        public async Task PoolFirstAiring()
        {
            // With a ready authored station_id row in the fake catalog, the drain airs the
            // authored MediaItem directly — no TTS render for the ident.
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var scope = new LibraryScope([1L]);
            var authoredIdent = new MediaReference(
                "42", "/imaging/ident.wav", "Station Ident", new Loudness(-14.0, -1.0, true),
                DurationMs: 5000, SampleRate: null, Channels: null, BitrateKbps: null,
                Artist: "Legacy Voice", Album: null, Genre: null, Year: null);
            var catalog = new FakeMediaCatalog(MakeTrackRef("t1")) { ImagingPoolResult = authoredIdent };
            var orchestrator = BuildOrchestrator(queue, clock, tts, catalog);

            queue.Enqueue(SpeechDeferralKind.StationId, "cadence: test");

            var first = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(first);
            Assert.Equal(authoredIdent.MediaId, first!.MediaId);
            Assert.Equal(SegmentKind.StationId, first.SegmentKind);
            Assert.Empty(tts.Requests); // pool-first — no TTS render at all for this ident

            // The SAME scope the music pick uses (Station:Scope) — never a separate safe scope.
            var call = Assert.Single(catalog.ImagingKindCalls);
            Assert.Equal(scope.LibraryIds, call.Scope.LibraryIds);
            Assert.Equal(ImagingKind.StationId, call.Kind);
        }

        [Fact]
        public async Task EmptyPoolFallsBackToTheTemplatedIdent()
        {
            // Empty pool ⇒ today's templated TTS ident renders unchanged (station voice,
            // never LLM-authored).
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var catalog = new FakeMediaCatalog(MakeTrackRef("t1")); // ImagingPoolResult left null
            var orchestrator = BuildOrchestrator(queue, clock, tts, catalog);

            queue.Enqueue(SpeechDeferralKind.StationId, "cadence: test");

            var first = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(first);
            var request = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.StationId);
            Assert.Equal("default", request.Voice); // the station's own identity voice, gh-#96
            Assert.Null(request.PersonaName); // never persona-voiced, never LLM-authored
        }

        [Fact]
        public async Task PoolFirstIdentNeverJumpsAheadOfAnAlreadyKickedBackAnnounce()
        {
            // Regression pin: a pool-first ident resolves INSTANTLY (no TTS render to await), but
            // must still respect Kick/KickResolved CALL order, not completion order — otherwise it
            // would race ahead of a back-announce that was Kicked earlier in the SAME unit but is
            // still awaiting its own (fake, but still awaited) TTS render.
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var authoredIdent = new MediaReference(
                "77", "/imaging/ident2.wav", "Station Ident", new Loudness(-14.0, -1.0, true),
                DurationMs: 5000, SampleRate: null, Channels: null, BitrateKbps: null,
                Artist: "Legacy Voice", Album: null, Genre: null, Year: null);
            var catalog = new FakeMediaCatalog(MakeTrackRef("t1")) { ImagingPoolResult = authoredIdent };
            var cadence = new CadenceConfig
            {
                LeadInBeforeEachTrack = false,
                BackAnnounceAfterEachTrack = true,
                StationIdEveryNUnits = 0,
            };
            var orchestrator = BuildOrchestrator(queue, clock, tts, catalog, cadence);

            // Unit 1: no prior track ⇒ no back-announce, no deferral pending ⇒ just the music track.
            var unit1Music = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(unit1Music);

            // Arm a StationId deferral for unit 2, where a back-announce for unit 1's track is ALSO
            // due (Kicked FIRST, per the class's own documented cadence order).
            queue.Enqueue(SpeechDeferralKind.StationId, "cadence: test");

            var backAnnounce = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            var pooledIdent = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            var unit2Music = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(backAnnounce);
            Assert.StartsWith("tts:backannounce", backAnnounce!.MediaId, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(pooledIdent);
            Assert.Equal(authoredIdent.MediaId, pooledIdent!.MediaId);
            Assert.NotNull(unit2Music);
            Assert.False(unit2Music!.MediaId.StartsWith("tts:", StringComparison.Ordinal));
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
