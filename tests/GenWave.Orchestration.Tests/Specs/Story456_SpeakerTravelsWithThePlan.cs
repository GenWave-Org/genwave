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
    // AC13 — dev-station wire evidence, ROUND 2 (T529 review finding F5): the round-1 run flipped
    // day 6's personaId every ~1s for 13 minutes, which oscillated across each cited row's own
    // occurred_at and could not tell a correct implementation from a broken one. This run instead
    // flips ONCE per trial and HOLDS — never back — in both directions. Round-2 finding B1: that
    // still does not discriminate, because the ~8s flip-to-row gap sits inside the card caches' 30s
    // StalenessBound and inside the per-unit-plan ResolveAsync horizon. What the consts DO establish
    // is by construction (the snapshot arm ran; pace was frozen at plan time), not by timing. The two
    // consts below carry the full timeline, row table, sampling method, and that limit; nothing here
    // restates them.
    // NOTE: the "manual: " prefix lives on BoothLogEvidence/PaceEvidence themselves (not here) —
    // stack_gate.sh's count_manual_facts_in_file (SPEC F182.1's prefix law; F182.3 ties the count to
    // gate-report.md's line) text-scans for a const whose OWN literal starts with `"manual: `; a
    // shared Preamble that itself carried the prefix would leave the two consts that are actually
    // named in Skip= unmatched by that scan (round-2 gate finding).
    const string Preamble =
        "dev-station wire, T529 round 2 (STORY-456, review finding F5) — verified live " +
        "2026-09-19 on the local docker dev stack, this branch (not Dean's demo box). Two personas, " +
        "distinct voice AND pace (T529r2 Alpha: hf_alpha@0.85, T529r2 Beta: hf_beta@1.15) on " +
        "segment_schedule day 6. ";

    const string Coda = " A real engine render cannot be a CI fact.";

    const string BoothLogEvidence = "manual: " + Preamble +
        "Redesign: flip ONCE and HOLD, never oscillate — landed each flip inside the narrow plan-" +
        "render window by pausing the kokoro container (docker pause, a cgroup freeze that stalls " +
        "the in-flight TTS call without killing the socket) the instant the api log showed it kick " +
        "off (\"Start processing HTTP request POST http://kokoro:8880/v1/audio/speech\"), firing the " +
        "PUT /api/schedule flip immediately, then unpausing seconds later to let the stalled render " +
        "complete and publish — zero production-code changes, a genuine (if artificially delayed) " +
        "Kokoro call. Sampling method for \"persona the schedule was written to, and when\": " +
        "CachingScheduleResolver invalidates synchronously and exclusively inside ScheduleRepository's " +
        "own write, so a PUT's HTTP 200 proves only that the write landed at that timestamp — it does " +
        "NOT prove the new persona is active yet: the subscriber is `void OnWeekChanged() => " +
        "dirty = true` (CachingScheduleResolver.cs:249), which marks the cache stale and nothing " +
        "more; activation lags until the next ResolveAsync (at most one unit plan away, per the " +
        "class's own remarks at :201-206) and, for the three card caches (ActivePersonaPaceCache, " +
        "ActivePersonaPronunciationRulesCache, ActivePersonaCorrectionsCache), until their 30s " +
        "StalenessBound elapses. Each write below is confirmed, where possible, by the FIRST later " +
        "row naming a persona voice that is not itself one of the four rows under test — step 3 is " +
        "the exception (see below). Timeline: 08:54:40.267 PUT->alpha (confirmed by row 94729, " +
        "08:55:39.831, settle 59s); 08:57:51.689 PUT->beta (row 94735, 08:59:05.967, settle 74s); " +
        "09:02:24.336 PUT->alpha, HELD (no independent confirmation — the nearest candidate, row " +
        "94745, is itself one of the four rows under test below, and its occurred_at of 09:10:01.597 " +
        "falls AFTER the next PUT, so using it to bound this step assumes the very \"the row's voice " +
        "names the planned persona\" proposition this fact is testing); 09:09:53.822 PUT->beta, HELD " +
        "(confirmed by the following control row 94751, 09:13:50.839). Trial 2: break planned under " +
        "BETA (active 08:57:51-09:02:24, settled 4m33s before its own plan). Rows 94739 (LeadIn) and " +
        "94740 (BackAnnounce), occurred_at 09:02:32.653014/.656709, both read \"voice: hf_beta\" — " +
        "8.32s AFTER the 09:02:24.336 PUT wrote the schedule to ALPHA (held, never reverted, through " +
        "09:09:53). TtsSegmentSource.LogRenderOutcome corroborates at the same instant: " +
        "persona=\"T529r2 Beta\" for both kinds at 09:02:32.653. Trial 3 (the mirror): break planned " +
        "under ALPHA (active 09:02:24-09:09:53, settled 7m29s). Rows 94744 (BackAnnounce) and 94745 " +
        "(LeadIn), occurred_at 09:10:01.593212/.597099, both read \"voice: hf_alpha\" — 7.77s AFTER " +
        "the 09:09:53.822 PUT wrote the schedule to BETA (held through 09:13:50+). LogRenderOutcome: " +
        "persona=\"T529r2 Alpha\" for both kinds at 09:10:01.592-594. Per-row table (id | occurred_at " +
        "| voice term | persona the schedule was written to, and when | how sampled): 94739 | " +
        "09:02:32.653014 | hf_beta | Alpha, written 09:02:24.336 | schedule-write timestamp, not an " +
        "activation sample; 94740 | 09:02:32.656709 | hf_beta | Alpha, written 09:02:24.336 | same; " +
        "94744 | 09:10:01.593212 | hf_alpha | Beta, written 09:09:53.822 | same; 94745 | " +
        "09:10:01.597099 | hf_alpha | Beta, written 09:09:53.822 | same (persona_id is null by design " +
        "on all four rows — not queried as evidence). Consequently: at this ~8s gap (8.32s for " +
        "94739/94740, 7.77s for 94744/94745) the flip sits inside BOTH the ResolveAsync-per-unit-plan " +
        "horizon and the three card caches' 30s StalenessBound — well under the 59s/74s settle times " +
        "this same run measured directly at steps 1 and 2 (rows 94729, 94735) — so a broken, still-" +
        "re-resolving implementation would have returned the identical pre-flip persona these four " +
        "rows show: the rows do not discriminate the snapshot arm from the ambient one, and stand " +
        "only as a plan-time-stamp regression guard, not as timing evidence for which arm rendered " +
        "them. F3 positive witness (round-2 finding): request.Speaker was non-null for all " +
        "four requests by construction, not by an absent-WARN inference — GenWave.Tts's service " +
        "collection registers ISpeakerSnapshotSource as a TryAddSingleton (never absent in this " +
        "Host), BreakPlanner.PlanAsync builds a SpeakerResolution whenever that source is non-null " +
        "(BreakPlanner.cs:91), day 6 named a real persona id (9 or 10, never a station-only slot) " +
        "for the whole run, and the api logs carry zero \"has no card\"/\"card lookup failed\" " +
        "degrade warnings. TtsSegmentSource.RenderCopyAsync (:249-251) branches on exactly " +
        "request.Speaker is {} — non-null routes to ResolveFromSnapshot (frozen speaker.Pace/Rules, " +
        "key ComputeSnapshotHash(text, voice, stationId, speaker.ContentHash)); null routes to " +
        "ResolveFromAmbientAsync (live personaPace.Current, key TtsSegmentSource's own five-term " +
        "ComputeHash) — a structurally disjoint key space. So the snapshot arm, not the ambient " +
        "re-resolving one, is what rendered all four cited rows. Honest scope: the row's voice is " +
        "request.Voice, stamped by BreakPlanner inside PlanAsync — a mechanism that predates F189 " +
        "(SPEC F35.3/F39.1's ResolvePersonaAsync) — and the cited rows are BackAnnounce/LeadIn, both " +
        "stamped in the SAME PlanAsync as their own break. A good regression guard, but it does not " +
        "exercise the seam T524-527 actually moved to a deferred read; AC6/AC7's SignOn/Context " +
        "drain is covered separately, in-process, by ScenarioASignOnArmedForBWhileAIsActive and " +
        "ScenarioAContextSegmentPlannedUnderA above." + Coda;

    const string PaceEvidence = "manual: " + Preamble +
        "Route (a), same copy through both personas: no production door reaches TtsSegmentSource " +
        "with arbitrary text under a chosen persona without a code change. POST /api/tts/preview and " +
        "POST /api/safe-segments both call ITtsSynthesizer directly, bypassing TtsSegmentSource's " +
        "two-arm branch entirely — the exact below-the-graph shortcut round-2 finding F1 ruled out — " +
        "though not identically: TtsPreviewController.BuildAuditionContextAsync (:178-188) still " +
        "awaits personaPace.RefreshIfStaleAsync and passes personaPace.Current, so /preview routes " +
        "through the ambient pace cache and NormalizingTtsSynthesizer rather than hitting kokoro-" +
        "fastapi bare. Either way it cannot reach the snapshot arm, and SafeSegmentAuthor " +
        "additionally leaves pace at TtsRenderContext's default of 1.0, never reading any persona's " +
        "Voice.Pace. Route (a) was not available. Route (b), character-normalised rates: captured " +
        "the real synthesis artifacts for the SAME break each trial straddled — the render whose " +
        "outbound Kokoro POST was deliberately stalled (docker pause) until after that trial's flip, " +
        "then released — genuine BreakPlanner-driven renders, not a direct Kokoro hit. Trial 2 " +
        "(planned beta@1.15): two cache files, ffprobe durations 1.971792s and 2.396042s. Trial 3 " +
        "(planned alpha@0.85): 2.812792s and 4.134958s — right direction (alpha slower) but I am " +
        "declining a seconds-per-character ratio: SegmentGenerated publishes at render completion " +
        "(TtsSegmentSource.cs:337), not air time, and this stack's own feeder can render a break " +
        "measurably ahead of the boundary it narrates (observed directly on an untouched control " +
        "break: rendered 08:55:39, but the track its own LeadIn would name did not start until " +
        "08:59:04, 3m25s later) — so I cannot safely reconstruct which cache file is LeadIn vs " +
        "BackAnnounce, or which real track's title/artist populates its copy text, from booth_log " +
        "adjacency alone; both trials' two candidate files also share an identical filesystem mtime " +
        "to the microsecond, so file metadata cannot break the tie either. Rather than dress up an " +
        "uncertain pairing as a clean ~1.35x proof, I am reporting the durations only, with no " +
        "derived ratio: AC13's pace conjunct has no clean independently-measured wire ratio from " +
        "this run. What IS proven, by construction rather than measurement: ResolveFromSnapshot " +
        "returns (speaker.Rules, speaker.Pace, hash) as ONE tuple (TtsSegmentSource.cs:364-382), and " +
        "RenderCopyAsync consumes rules/pace/hash from exactly one of its two branches, never a mix " +
        "— so the same non-null-Speaker fact the BoothLogEvidence const establishes for all four " +
        "cited requests also means pace for all four was speaker.Pace, frozen at plan time, never " +
        "personaPace.Current (the live read T527 closed), regardless of the flip. TtsSegmentSource's " +
        "voice-mismatch WARN (\"differs from the request voice\") never appeared in the api logs " +
        "across the whole run (08:36:49 onward) — consistent, though absence alone is not proof " +
        "(round-2 finding F3)." + Coda;

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
        // Given: two personas of different pace and voice; flip the SCHEDULE's active persona ONCE
        // after a break is planned but before it renders, then HOLD — never flip back. The write is
        // what flips; activation lags it (round-2 finding B1, see BoothLogEvidence). (manual, T529 rd 3)

        /// <summary>AC13 — a break's speech rows name the persona that was PLANNED, not one flipped to mid-render</summary>
        [Fact(Skip = BoothLogEvidence)]
        public void TheBoothLogNamesThePlannedPersona() => Assert.Fail(BoothLogEvidence);

        /// <summary>AC13 — the rendered audio's pace matches the planned persona's, not a pace flipped to mid-render</summary>
        [Fact(Skip = PaceEvidence)]
        public void TheAudioPaceMatchesThePlan() => Assert.Fail(PaceEvidence);
    }
}
