// STORY-457 — HandoffCeremonyProducer arms the ceremony (gh-#401 · SPEC F190 · PLAN T532)
//
// BDD specification — xUnit. AC1–AC7 drive ArmAsync per dedupe row; AC8/AC9 the capture and hold; AC10 warn-once; AC11 reflects the
// constant; AC12 is the existing F92/F142/F112 suite.
//
// GREEN (T532): HandoffCeremonyProducer is directly constructible with this project's own TestSupport
// fakes (SPEC F190.5) — every fact below except AC12 builds one straight from a
// CachingScheduleResolver/FakeScheduleStore/FakePersonaStore triple, no Orchestrator/OrchestratorBuilder
// involved. AC12 alone reaches through FeatureDjsHandOffAudibly's own production chain (STORY-243, PLAN
// T124) — proof the pre-T532 wiring still delivers a full ceremony now that it arms entirely through
// this producer.

using System.Reflection;
using Microsoft.Extensions.Time.Testing;
using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureHandoffCeremonyProducer
{
    // ---------------------------------------------------------------------
    // Harness — a bare HandoffCeremonyProducer wired directly to this project's own fakes (SPEC
    // F190.5), no Orchestrator/OrchestratorBuilder in the loop. Mirrors the CachingScheduleResolver/
    // ScheduleResolver/FakeScheduleStore construction FeatureDjsHandOffAudibly's own BuildProductionChain
    // uses (STORY-243, PLAN T124), scoped down to just the seams this producer itself takes.
    // ---------------------------------------------------------------------

    internal sealed record Harness(
        HandoffCeremonyProducer Producer,
        SpeechDeferralQueue Queue,
        FakeTimeProvider Time,
        FakeBoundaryBiasProvider BoundaryBias,
        CapturingLogger<HandoffCeremonyProducer> Logger);

    static readonly StationIdentity Identity = new("s1", "GenWave", "default");

    static readonly DayOfWeek Monday = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero).DayOfWeek;

    // 5 minutes before the noon boundary — inside a 10-minute F74.3 lookahead window from the very
    // first unit planned (the same instant FeatureDjsHandOffAudibly's own JustBeforeNoon names).
    static readonly DateTimeOffset JustBeforeNoon = new(2026, 3, 2, 11, 55, 0, TimeSpan.Zero);

    static Persona MakePersona(long id, string name, string voice)
    {
        var now = DateTime.UnixEpoch;
        return new Persona(id, name, "", "", voice, now, now);
    }

    static Harness Build(FakePersonaStore personaStore, ScheduleWeekSnapshot snapshot, DateTimeOffset now, TimeSpan lookahead)
    {
        var time = new FakeTimeProvider(now);
        var scheduleStore = new FakeScheduleStore(snapshot);
        var stationDefault = new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault);
        var resolver = new ScheduleResolver(time, stationDefault);
        var caching = new CachingScheduleResolver(scheduleStore, resolver, new FakeScheduleSpecialStore());
        var queue = new SpeechDeferralQueue(time);
        var boundaryBias = new FakeBoundaryBiasProvider(lookahead);
        var logger = new CapturingLogger<HandoffCeremonyProducer>();
        var producer = new HandoffCeremonyProducer(
            queue, boundaryBias, logger, scheduleResolver: caching, personaStore: personaStore);

        return new Harness(producer, queue, time, boundaryBias, logger);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the dedupe matrix (SPEC F92.3, F114.3/F116.2)
    // ---------------------------------------------------------------------

    public sealed class ScenarioGapToGap
    {
        // Given: no show either side — a genuine grid gap (00:00-12:00, nothing scheduled) followed by
        // an EXPLICIT persona-less segment (12:00-24:00), the F92.3 gap-to-gap row.

        static ScheduleWeekSnapshot GapToGapSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: null, Genres: ["Ambient"], EnergyMin: null, EnergyMax: null),
        ]);

        /// <summary>AC1 — both persona ids null: nothing arms.</summary>
        [Fact]
        public async Task ArmsNothing()
        {
            var harness = Build(new FakePersonaStore(), GapToGapSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            await harness.Producer.ArmAsync(Identity, harness.Time.GetUtcNow(), CancellationToken.None);

            Assert.Null(harness.Queue.Peek(SpeechDeferralKind.SignOff));
            Assert.Null(harness.Queue.Peek(SpeechDeferralKind.SignOn));
        }
    }

    public sealed class ScenarioSelfHandoff
    {
        // Given: same persona, same show (the F91.6 seeded grid's own midnight-roll shape, both rows
        // showless here).

        static ScheduleWeekSnapshot SamePersonaSameShowSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        /// <summary>AC2 — persona ids equal and non-null, show ids equal (both showless): nothing arms.</summary>
        [Fact]
        public async Task ArmsNothing()
        {
            var store = new FakePersonaStore();
            store.Add(MakePersona(10, "DJ Alpha", "af_alpha"));
            var harness = Build(store, SamePersonaSameShowSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            await harness.Producer.ArmAsync(Identity, harness.Time.GetUtcNow(), CancellationToken.None);

            Assert.Null(harness.Queue.Peek(SpeechDeferralKind.SignOff));
            Assert.Null(harness.Queue.Peek(SpeechDeferralKind.SignOn));
        }
    }

    public sealed class ScenarioSamePersonaDifferentShow
    {
        // Given: persona A, show X then Y — F114.3/F116.2's same-persona show-transition row.

        static ScheduleWeekSnapshot ShowXThenYSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null, ShowId: 100),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null, ShowId: 200),
        ]);

        /// <summary>AC3 — same persona, DIFFERENT show ids: arms a sign-on only, styled as a
        /// transition — no counterpart (there is no OTHER persona to hand off to).</summary>
        [Fact]
        public async Task ArmsASignOnOnly()
        {
            var store = new FakePersonaStore();
            store.Add(MakePersona(10, "DJ Alpha", "af_alpha"));
            var harness = Build(store, ShowXThenYSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            await harness.Producer.ArmAsync(Identity, harness.Time.GetUtcNow(), CancellationToken.None);

            Assert.Null(harness.Queue.Peek(SpeechDeferralKind.SignOff));
            var signOn = harness.Queue.Peek(SpeechDeferralKind.SignOn);
            Assert.True(signOn is { Handoff.Voice: "af_alpha", Handoff.CounterpartName: null });
        }
    }

    public sealed class ScenarioShowThenGap
    {
        // Given: show X then nothing — into music-only (F92.3's one-sided sign-off-only row).

        static ScheduleWeekSnapshot ShowThenGapSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: null, Genres: ["Ambient"], EnergyMin: null, EnergyMax: null),
        ]);

        /// <summary>AC4 — outgoing non-null, incoming null: arms a sign-off only, no counterpart
        /// ("the music keeps rolling").</summary>
        [Fact]
        public async Task ArmsASignOffOnly()
        {
            var store = new FakePersonaStore();
            store.Add(MakePersona(10, "DJ Alpha", "af_alpha"));
            var harness = Build(store, ShowThenGapSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            await harness.Producer.ArmAsync(Identity, harness.Time.GetUtcNow(), CancellationToken.None);

            var signOff = harness.Queue.Peek(SpeechDeferralKind.SignOff);
            Assert.True(signOff is { Handoff.Voice: "af_alpha", Handoff.CounterpartName: null });
            Assert.Null(harness.Queue.Peek(SpeechDeferralKind.SignOn));
        }
    }

    public sealed class ScenarioGapThenShow
    {
        // Given: nothing then show Y — out of music-only (F92.3's one-sided sign-on-only row).

        static ScheduleWeekSnapshot GapThenShowSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: null, Genres: ["Ambient"], EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        /// <summary>AC5 — outgoing null, incoming non-null: arms a sign-on only, no counterpart
        /// ("no predecessor").</summary>
        [Fact]
        public async Task ArmsASignOnOnly()
        {
            var store = new FakePersonaStore();
            store.Add(MakePersona(20, "DJ Beta", "af_beta"));
            var harness = Build(store, GapThenShowSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            await harness.Producer.ArmAsync(Identity, harness.Time.GetUtcNow(), CancellationToken.None);

            Assert.Null(harness.Queue.Peek(SpeechDeferralKind.SignOff));
            var signOn = harness.Queue.Peek(SpeechDeferralKind.SignOn);
            Assert.True(signOn is { Handoff.Voice: "af_beta", Handoff.CounterpartName: null });
        }
    }

    public sealed class ScenarioTwoShowsTwoPersonas
    {
        // Given: show X (persona A) then show Y (persona B) — both non-null, different personas: both
        // pieces arm, each naming the OTHER persona as counterpart.

        static ScheduleWeekSnapshot TwoDjSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        static FakePersonaStore TwoDjStore()
        {
            var store = new FakePersonaStore();
            store.Add(MakePersona(10, "DJ Alpha", "af_alpha"));
            store.Add(MakePersona(20, "DJ Beta", "af_beta"));
            return store;
        }

        /// <summary>AC6 — arms a sign-off naming the incoming persona as counterpart.</summary>
        [Fact]
        public async Task ArmsASignOff()
        {
            var harness = Build(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            await harness.Producer.ArmAsync(Identity, harness.Time.GetUtcNow(), CancellationToken.None);

            var signOff = harness.Queue.Peek(SpeechDeferralKind.SignOff);
            Assert.True(signOff is { Handoff.Voice: "af_alpha", Handoff.PersonaName: "DJ Alpha", Handoff.CounterpartName: "DJ Beta" });
        }

        /// <summary>AC6 — arms a sign-on naming the outgoing persona as counterpart.</summary>
        [Fact]
        public async Task ArmsASignOn()
        {
            var harness = Build(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            await harness.Producer.ArmAsync(Identity, harness.Time.GetUtcNow(), CancellationToken.None);

            var signOn = harness.Queue.Peek(SpeechDeferralKind.SignOn);
            Assert.True(signOn is { Handoff.Voice: "af_beta", Handoff.PersonaName: "DJ Beta", Handoff.CounterpartName: "DJ Alpha" });
        }
    }

    public sealed class ScenarioTheSameBoundaryArmedTwice
    {
        // Given: ArmAsync twice against the SAME unchanged (BoundaryAt, persona, show) tuple.

        static ScheduleWeekSnapshot TwoDjSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        /// <summary>AC7 — the arm-once guard (T124 review finding F2): a second call against the
        /// unchanged tuple touches neither the queue nor personaStore again — both pieces are still
        /// held, and personaStore saw no further lookups past the first call's two.</summary>
        [Fact]
        public async Task HoldsEachKindOnce()
        {
            var store = new FakePersonaStore();
            store.Add(MakePersona(10, "DJ Alpha", "af_alpha"));
            store.Add(MakePersona(20, "DJ Beta", "af_beta"));
            var harness = Build(store, TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));
            var now = harness.Time.GetUtcNow();

            await harness.Producer.ArmAsync(Identity, now, CancellationToken.None);
            var callsAfterFirstArm = store.GetByIdCalls.Count;

            await harness.Producer.ArmAsync(Identity, now, CancellationToken.None);

            Assert.Equal(callsAfterFirstArm, store.GetByIdCalls.Count);
            Assert.NotNull(harness.Queue.Peek(SpeechDeferralKind.SignOff));
            Assert.NotNull(harness.Queue.Peek(SpeechDeferralKind.SignOn));
        }
    }

    public sealed class ScenarioACrossingTrackCaptured
    {
        // Given: CaptureCrossingTrack(track) on an armed SignOn.

        static ScheduleWeekSnapshot GapThenShowSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: null, Genres: ["Ambient"], EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        /// <summary>AC8 — stamps the crossing track's title/artist into the held SignOn's own
        /// HandoffContext (SPEC F111.3).</summary>
        [Fact]
        public async Task StampsTheCrossingTitle()
        {
            var store = new FakePersonaStore();
            store.Add(MakePersona(20, "DJ Beta", "af_beta"));
            var harness = Build(store, GapThenShowSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));
            await harness.Producer.ArmAsync(Identity, harness.Time.GetUtcNow(), CancellationToken.None);
            var track = new MediaItem(
                "t1", "/media/t1.mp3", "Crossing Track", new Loudness(-23.0, -1.0, true), Artist: "Crossing Artist");

            harness.Producer.CaptureCrossingTrack(track);

            var signOn = harness.Queue.Peek(SpeechDeferralKind.SignOn);
            Assert.True(signOn is { Handoff.CrossingTrackTitle: "Crossing Track", Handoff.CrossingTrackArtist: "Crossing Artist" });
        }
    }

    public sealed class ScenarioASignOnHeldPastTheTail
    {
        // Given: HoldSignOnPastQueuedTail(now, 40s) with the F74.3 lookahead shrunk to 20s.

        static ScheduleWeekSnapshot GapThenShowSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: null, Genres: ["Ambient"], EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        /// <summary>AC9 — clamped: the hold's own NotBefore never reaches further out than
        /// boundaryBiasProvider's own F74.3 lookahead, even when the raw queued tail would carry it
        /// further (round-2 review finding F5).</summary>
        [Fact]
        public async Task SetsNotBeforeToNowPlusTheTail()
        {
            var store = new FakePersonaStore();
            store.Add(MakePersona(20, "DJ Beta", "af_beta"));
            var harness = Build(store, GapThenShowSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));
            await harness.Producer.ArmAsync(Identity, harness.Time.GetUtcNow(), CancellationToken.None);
            var now = harness.Time.GetUtcNow();
            harness.BoundaryBias.Lookahead = TimeSpan.FromSeconds(20); // shrinks the clamp below the raw 40s tail

            harness.Producer.HoldSignOnPastQueuedTail(now, TimeSpan.FromSeconds(40));

            var signOn = harness.Queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(signOn);
            Assert.Equal((DateTimeOffset?)(now + TimeSpan.FromSeconds(20)), signOn.NotBefore);
        }
    }

    public sealed class ScenarioTheConstantOnTheOrchestrator
    {
        // Given: typeof(Orchestrator).SignOffLeadTime

        /// <summary>AC11 — the field this producer's own ArmAsync reads (SPEC F190.4) stays public
        /// static on Orchestrator, unmoved, at fifteen seconds.</summary>
        [Fact]
        public void IsAPublicStaticFifteenSeconds()
        {
            var field = typeof(Orchestrator).GetField(
                nameof(Orchestrator.SignOffLeadTime), BindingFlags.Public | BindingFlags.Static);

            Assert.NotNull(field);
            Assert.True(field.IsPublic);
            Assert.True(field.IsStatic);
            var value = field.GetValue(null);
            Assert.NotNull(value);
            Assert.Equal(TimeSpan.FromSeconds(15), Assert.IsType<TimeSpan>(value));
        }
    }

    public sealed class ScenarioTheOldFacts
    {
        // Given: F92 / F142 / F112 specs — FeatureDjsHandOffAudibly's own production chain (STORY-243,
        // PLAN T124), unchanged by the T532 extraction.

        /// <summary>AC12 — the pre-T532 wiring still delivers a full two-piece ceremony now that
        /// Orchestrator arms it entirely through HandoffCeremonyProducer: proof this really was a pure
        /// move, exercised end-to-end through the SAME production chain those specs build.</summary>
        [Fact]
        public async Task StayGreenWithTheProducer()
        {
            var chain = FeatureDjsHandOffAudibly.BuildProductionChain(
                FeatureDjsHandOffAudibly.TwoDjStore(), FeatureDjsHandOffAudibly.TwoDjSchedule(),
                FeatureDjsHandOffAudibly.JustBeforeNoon, TimeSpan.FromMinutes(10));

            var items = await FeatureDjsHandOffAudibly.PullUnitsAsync(
                chain.Orchestrator, chain.Time, FeatureDjsHandOffAudibly.PullStep, FeatureDjsHandOffAudibly.PullCount);

            Assert.Contains(items, FeatureDjsHandOffAudibly.IsSignOff);
            Assert.Contains(items, FeatureDjsHandOffAudibly.IsSignOn);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — segregated
    // ---------------------------------------------------------------------

    public sealed class ScenarioNoScheduleResolver
    {
        // Given: ArmAsync three times without a resolver.

        /// <summary>AC10 — a null scheduleResolver makes ArmAsync a permanent no-op, WARN-logged
        /// exactly once for the life of the producer (T124 review finding F7), never again.</summary>
        [Fact]
        public async Task WarnsExactlyOnce()
        {
            var time = new FakeTimeProvider(JustBeforeNoon);
            var queue = new SpeechDeferralQueue(time);
            var logger = new CapturingLogger<HandoffCeremonyProducer>();
            var producer = new HandoffCeremonyProducer(
                queue, new FakeBoundaryBiasProvider(TimeSpan.FromMinutes(10)), logger, scheduleResolver: null);

            await producer.ArmAsync(Identity, time.GetUtcNow(), CancellationToken.None);
            await producer.ArmAsync(Identity, time.GetUtcNow(), CancellationToken.None);
            await producer.ArmAsync(Identity, time.GetUtcNow(), CancellationToken.None);

            Assert.Single(logger.Warnings);
        }
    }
}
