// STORY-456 — The speaker travels with the plan (gh-#772 · SPEC F189.1, F189.4, F189.5, F189.7 · PLAN T524, T527)
//
// BDD specification — xUnit. AC1 pins the additive contract; AC6–AC9 drive the planner and the ceremony arm with a counting snapshot source
// and a source that flips the active persona at render time. The render branch is Tts.Tests
// (Story456_SnapshotDrivesTheRender); card-by-id is Host.Tests (Story456_PersonaCardById); the seam index is
// Architecture.Tests (Story456_SeamIndex). AC13 is the dev-station wire (T529).

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureSpeakerTravelsWithThePlan
{
    const string Manual = "manual: dev-station wire, T529 — booth_log rows + stereo capture (STORY-456)";

    static readonly StationIdentity Identity = new("station-1", "GenWave", "voice-station");
    static readonly HashSet<SpeechDeferralKind> NoHolds = [];

    static CadenceConfig Cadence(bool leadIn = true, bool backAnnounce = true, int stationIdEveryN = 0) => new()
    {
        LeadInBeforeEachTrack = leadIn,
        BackAnnounceAfterEachTrack = backAnnounce,
        StationIdEveryNUnits = stationIdEveryN,
    };

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioASegmentRequestBuiltTheOldWay
    {
        // Given: SegmentRequest with today's arguments — the published 14-arg ctor alone, no
        // object-initializer touching the additive Speaker member at all.

        /// <summary>AC1 — a request built through the pre-F189 constructor alone carries no Speaker</summary>
        [Fact]
        public void HasNoSpeaker()
        {
            var request = new SegmentRequest(
                SegmentKind.BackAnnounce, "af_default", "GenWave", null, DateTimeOffset.UtcNow, "station-1");

            Assert.Null(request.Speaker);
        }
    }

    public sealed class ScenarioASignOnArmedForBWhileAIsActive
    {
        // Given: a SignOn deferral armed at enqueue time carrying B's own captured HandoffContext.Speaker,
        // drained while A is the ambient active persona (AC6 proves the drain forwards the CAPTURED
        // snapshot, never a fresh resolve off the ambient accessor).

        static readonly SpeakerSnapshot BSnapshot = new(20, "DJ Beta", "af_beta", 1.0, [], [], "persona-20");

        /// <summary>AC6 — the drained SignOn request's Speaker is B's captured snapshot, not A's ambient one</summary>
        [Fact]
        public async Task RendersWithBsSnapshot()
        {
            var personaAccessor = new FakeActivePersonaAccessor { Persona = TestData.MakePersona(10, "DJ Alpha", "af_alpha") };
            var chain = new BreakPlannerBuilder().WithPersonaAccessor(personaAccessor).Build();

            chain.Queue.Enqueue(
                SpeechDeferralKind.SignOn, "test: arm for B",
                handoff: new HandoffContext("af_beta", "DJ Beta", "DJ Alpha", Speaker: BSnapshot));

            var now = chain.Time.GetUtcNow();
            var breakContext = new BreakContext(1, null, null, "DJ Alpha", Cadence(leadIn: false, backAnnounce: false), Identity, now, null, NoHolds, TimeSpan.Zero);
            var plan = await chain.Planner.PlanAsync(breakContext, CancellationToken.None);

            var signOn = Assert.Single(plan.Slots, s => s.Kind == SegmentKind.SignOn);
            Assert.Equal(20, (signOn.Source as RenderSource)?.Request.Speaker?.PersonaId);
        }
    }

    public sealed class ScenarioAHandoffWhereTheRowAndCardVoicesDisagree
    {
        // Given: a HandoffContext whose own Voice (the persona ROW, read by Orchestrator via
        // ResolveHandoffPersonaAsync) disagrees with its captured Speaker.Voice (the persona CARD,
        // read at arm time via ISpeakerSnapshotSource) — the two provably-diverging seams round-2
        // review finding F3 names (F79.4's blanked import voice; PersonaCardMigrator's card-less
        // default persona are both shipped examples of exactly this split).

        static readonly SpeakerSnapshot BCardSnapshot = new(20, "DJ Beta", "af_beta_card", 1.0, [], [], "persona-20");

        /// <summary>F3 — a drained SignOn request's Voice equals its own Speaker's Voice, even when the arm-time snapshot carried a different voice</summary>
        [Fact]
        public async Task VoiceAndSpeakerAgree()
        {
            var chain = new BreakPlannerBuilder().Build();

            chain.Queue.Enqueue(
                SpeechDeferralKind.SignOn, "test: row voice != card voice",
                handoff: new HandoffContext("af_beta_row", "DJ Beta", "DJ Alpha", Speaker: BCardSnapshot));

            var now = chain.Time.GetUtcNow();
            var breakContext = new BreakContext(1, null, null, "DJ Alpha", Cadence(leadIn: false, backAnnounce: false), Identity, now, null, NoHolds, TimeSpan.Zero);
            var plan = await chain.Planner.PlanAsync(breakContext, CancellationToken.None);

            var signOn = Assert.Single(plan.Slots, s => s.Kind == SegmentKind.SignOn);
            var request = ((RenderSource)signOn.Source).Request;
            Assert.Equal(request.Voice, request.Speaker?.Voice);
        }
    }

    public sealed class ScenarioAContextSegmentPlannedUnderA
    {
        // Given: a context provider explicitly configured to persona A (10) while persona B (20) is the
        // ambient active one — AC7 proves the context segment's Speaker resolves the EXPLICIT provider
        // persona, never the ambient accessor's.

        /// <summary>AC7 — the planned context segment's Speaker is A's (the explicitly configured persona), not B's (the ambient one)</summary>
        [Fact]
        public async Task RendersWithAsSnapshot()
        {
            var personaStore = new FakePersonaStore();
            personaStore.Add(TestData.MakePersona(10, "DJ Alpha", "af_alpha"));

            var contextSettings = new FakeContextSettingsProvider();
            contextSettings.Set("weather", new ContextProviderSettings(true, 10, 5, PersonaId: 10));

            var personaAccessor = new FakeActivePersonaAccessor { Persona = TestData.MakePersona(20, "DJ Beta", "af_beta") };

            var chain = new BreakPlannerBuilder()
                .WithPersonaAccessor(personaAccessor)
                .WithPersonaStore(personaStore)
                .WithContextSettings(contextSettings)
                .WithSpeakerSnapshotSource(new CountingSpeakerSnapshotSource())
                .Build();

            var now = chain.Time.GetUtcNow();
            chain.Queue.Enqueue(
                SpeechDeferralKind.Context, "test: weather due", discriminator: "weather",
                context: new ContextSegmentFacts("Sunny and mild.", now.AddMinutes(30)));

            var breakContext = new BreakContext(1, null, null, "DJ Beta", Cadence(leadIn: false, backAnnounce: false), Identity, now, null, NoHolds, TimeSpan.Zero);
            var plan = await chain.Planner.PlanAsync(breakContext, CancellationToken.None);

            var segment = Assert.Single(plan.Slots, s => s.Kind == SegmentKind.ContextSegment);
            Assert.Equal(10, (segment.Source as RenderSource)?.Request.Speaker?.PersonaId);
        }
    }

    public sealed class ScenarioAnArmedCeremonyReArmed
    {
        // Given: a genuine re-arm (the SAME-PERSONA/DIFFERENT-SHOW branch, SPEC F116.2) over a still-
        // held SignOn — mirrors Story303_StraddleHandoff's own
        // TheSamePersonaBranchesReArmPreservesTheGateRatherThanBypassingIt almost verbatim, with a
        // CountingSpeakerSnapshotSource added so AC8 can prove the held speaker is REUSED, not
        // re-resolved, across the re-arm.

        static readonly DayOfWeek Monday = new DateTimeOffset(2030, 1, 7, 0, 0, 0, TimeSpan.Zero).DayOfWeek;
        static readonly DateTimeOffset JustBeforeNoon = new(2030, 1, 7, 11, 55, 0, TimeSpan.Zero);

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

        static ScheduleWeekSnapshot SamePersonaDifferentShowSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 723, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null, ShowId: 100),
            new ScheduleSegment(Id: 3, Day: Monday, StartMinute: 723, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null, ShowId: 200),
        ]);

        static FakePersonaStore TwoDjStore()
        {
            var now = DateTime.UnixEpoch;
            var store = new FakePersonaStore();
            store.Add(new Persona(10, "DJ Alpha", "", "", "af_alpha", now, now));
            store.Add(new Persona(20, "DJ Beta", "", "", "af_beta", now, now));
            return store;
        }

        static (Orchestrator Orchestrator, FakeTimeProvider Time, SpeechDeferralQueue Queue) BuildChain(
            FakeMediaCatalog catalog, ISpeakerSnapshotSource speakerSource)
        {
            var time = new FakeTimeProvider(JustBeforeNoon);
            var scheduleStore = new FakeScheduleStore(SamePersonaDifferentShowSchedule());
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));
            var caching = new CachingScheduleResolver(scheduleStore, resolver, new FakeScheduleSpecialStore());
            var queue = new SpeechDeferralQueue(time);
            var orchestrator = new OrchestratorBuilder()
                .WithIdentity(new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")))
                .WithScope(new FakeStationScopeProvider(new LibraryScope([1L])))
                .WithCadence(Cadence(leadIn: false, backAnnounce: false))
                .WithRotation(new FakeRotationSettingsProvider(new RotationSettings()))
                .WithMusicSelectionPolicy(new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance))
                .WithTts(new FakeTtsSegmentSource())
                .WithPersonaAccessor(new FakeActivePersonaAccessor())
                .WithLogger(NullLogger<Orchestrator>.Instance)
                .WithRenderBudget(TimeSpan.FromSeconds(30))
                .WithDeferralQueue(queue)
                .WithTime(time)
                .WithBoundaryBias(new FakeBoundaryBiasProvider(TimeSpan.FromMinutes(10)))
                .WithScheduleResolver(caching)
                .WithPersonaStore(TwoDjStore())
                .WithSpeakerSnapshotSource(speakerSource)
                .Build()
                .Orchestrator;

            return (orchestrator, time, queue);
        }

        /// <summary>AC8 — HandoffContext.Speaker survives a genuine re-arm (SPEC F189.5): the held
        /// counterpart's snapshot is reused, never re-resolved, even though the re-arm's own content
        /// (Due) genuinely refreshed to the new boundary.</summary>
        [Fact]
        public async Task KeepsTheOriginalSpeaker()
        {
            var crossing = MakeTrack("crossing", TimeSpan.FromMinutes(3));
            var catalog = FakeMediaCatalog.WithPool([crossing]);
            var source = new CountingSpeakerSnapshotSource();
            var (orchestrator, time, queue) = BuildChain(catalog, source);

            // Unit 1: arms the Alpha->Beta ceremony for noon — Beta's snapshot resolves here.
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Unit 2: 20 minutes already queued ahead — crosses. The SignOff airs; the paired (Beta)
            // SignOn is held, gated to the window bound (NotBefore = 12:05) — the arm-once guard means
            // this unit re-resolves nothing.
            await orchestrator.GetNextAsync(
                new PlayoutContext([], QueuedAheadMs: (int)TimeSpan.FromMinutes(20).TotalMilliseconds), CancellationToken.None);
            var held = queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(held);
            var heldDue = held.Due;

            // Real wall-clock time crosses noon — the resolver's own "current" flips to Beta/show-A,
            // whose OWN next block is Beta/show-B (SAME persona, DIFFERENT show): the same-persona
            // branch re-arms a FRESH show-transition sign-on over the SAME slot.
            time.Advance(TimeSpan.FromMinutes(6));
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var reArmed = queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(reArmed);
            Assert.NotEqual(heldDue, reArmed.Due); // genuine re-arm — the content refreshed to the new boundary
            Assert.Equal(1, source.PersonaCalls[20]); // ...but Beta's snapshot was reused, not re-resolved
        }
    }

    public sealed class ScenarioAPersonaCardVoiceDisagreesWithTheAccessor
    {
        // Given: the on-air persona's ROW (read via IActivePersonaAccessor, ResolvePersonaAsync's own
        // seam) names one voice, while that SAME persona's CARD (CountingSpeakerSnapshotSource.Personas
        // override, standing in for an uninstalled or otherwise unvalidated card voiceId — F79.4)
        // names a DIFFERENT one — round-2 review finding F1: the planner-resolved (row) voice must
        // win; the card's snapshot aligns to it, never the reverse.

        /// <summary>F1 — the stamped Voice is the planner-resolved voice (the persona ROW's), never the persona CARD's disagreeing one</summary>
        [Fact]
        public async Task TheStampedVoiceIsThePlannerResolvedOne()
        {
            var personaAccessor = new FakeActivePersonaAccessor { Persona = TestData.MakePersona(10, "DJ Alpha", "af_alpha") };
            var source = new CountingSpeakerSnapshotSource();
            source.Personas[10] = new SpeakerSnapshot(10, "DJ Alpha", "af_uninstalled_card_voice", 1.0, [], [], "persona-10");

            var chain = new BreakPlannerBuilder()
                .WithPersonaAccessor(personaAccessor)
                .WithSpeakerSnapshotSource(source)
                .Build();

            var now = chain.Time.GetUtcNow();
            var prev = TestData.MakeTrackRef("t1").ToMediaItem();
            var breakContext = new BreakContext(1, prev, null, "DJ Alpha", Cadence(leadIn: false), Identity, now, null, NoHolds, TimeSpan.Zero);
            var plan = await chain.Planner.PlanAsync(breakContext, CancellationToken.None);

            var backAnnounce = Assert.Single(plan.Slots, s => s.Kind == SegmentKind.BackAnnounce);
            Assert.Equal("af_alpha", ((RenderSource)backAnnounce.Source).Request.Voice);
        }
    }

    public sealed class ScenarioACountingSnapshotSource : IAsyncLifetime
    {
        // Given: a break naming the station voice TWICE (the Announcement, and the StationId cadence
        // drain — Cadence(stationIdEveryN: 1) at UnitOrdinal 1 arms and drains it in the same plan),
        // persona 7 twice (BackAnnounce + LeadIn, both driven by the SAME ambient active persona) and
        // persona 9 once (an explicitly configured Context provider) — AC9 proves each distinct id
        // resolves exactly once per plan, however many slots name it (SPEC F188.4's per-plan memo).
        // The station MUST be named twice for ResolvesTheStationOnce to actually pin the memo (round-2
        // review finding F2): a single namer leaves StationCalls == 1 whether or not the memo exists.

        readonly CountingSpeakerSnapshotSource source = new();
        SegmentRequest? backAnnounceRequest;

        public async Task InitializeAsync()
        {
            var personaStore = new FakePersonaStore();
            personaStore.Add(TestData.MakePersona(7, "DJ Seven", "af_seven"));
            personaStore.Add(TestData.MakePersona(9, "DJ Nine", "af_nine"));

            var contextSettings = new FakeContextSettingsProvider();
            contextSettings.Set("weather", new ContextProviderSettings(true, 10, 5, PersonaId: 9));

            var personaAccessor = new FakeActivePersonaAccessor { Persona = TestData.MakePersona(7, "DJ Seven", "af_seven") };

            var announcements = new FakeAnnouncementSource();
            announcements.Pending.Enqueue(new AnnouncementItem(1, "Happy birthday!", Verbatim: true, RequestedVoice: null));

            var chain = new BreakPlannerBuilder()
                .WithPersonaAccessor(personaAccessor)
                .WithPersonaStore(personaStore)
                .WithContextSettings(contextSettings)
                .WithAnnouncementSource(announcements)
                .WithSpeakerSnapshotSource(source)
                .Build();

            var now = chain.Time.GetUtcNow();
            chain.Queue.Enqueue(
                SpeechDeferralKind.Context, "test: weather due", discriminator: "weather",
                context: new ContextSegmentFacts("Sunny and mild.", now.AddMinutes(30)));

            var track = TestData.MakeTrackRef("t1").ToMediaItem();
            var breakContext = new BreakContext(1, track, track, "DJ Seven", Cadence(stationIdEveryN: 1), Identity, now, null, NoHolds, TimeSpan.Zero);
            var plan = await chain.Planner.PlanAsync(breakContext, CancellationToken.None);

            backAnnounceRequest = ((RenderSource)plan.Slots.Single(s => s.Kind == SegmentKind.BackAnnounce).Source).Request;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC9 — the station snapshot resolves exactly once, even though the same plan names it TWICE: once for the Announcement slot, once for the StationId cadence drain</summary>
        [Fact]
        public void ResolvesTheStationOnce() => Assert.Equal(1, source.StationCalls);

        /// <summary>AC9 — each distinct persona resolves exactly once per plan: persona 7 names it twice (BackAnnounce + LeadIn), persona 9 once (the context segment)</summary>
        [Fact]
        public void ResolvesEachPersonaOnce()
        {
            Assert.Equal(1, source.PersonaCalls[7]);
            Assert.Equal(1, source.PersonaCalls[9]);
        }

        /// <summary>Ruling 9 — a stamped request's Voice always matches its own Speaker's Voice, never a separately-resolved term</summary>
        [Fact]
        public void TheBackAnnounceVoiceMatchesItsSnapshot()
        {
            Assert.NotNull(backAnnounceRequest);
            Assert.Equal(backAnnounceRequest.Speaker?.Voice, backAnnounceRequest.Voice);
        }
    }

    public sealed class ScenarioTheDevStationWire
    {
        // Given: two personas of different pace, flip mid-break (manual, T529)

        /// <summary>AC13 — </summary>
        [Fact(Skip = Manual)]
        public void TheBoothLogNamesThePlannedPersona() => Assert.Fail(Manual);

        /// <summary>AC13 — </summary>
        [Fact(Skip = Manual)]
        public void TheAudioPaceMatchesThePlan() => Assert.Fail(Manual);
    }
}
