// STORY-243 — DJs hand off audibly (SPEC F92, PLAN T123/T124)
//
// BDD specification — xUnit. T123's facts are pure GenWave.Tts seams (prompts receive both display
// names, template fallbacks, blurb-dir routing, the F92.4/F92.5 null-render ruling for non-LLM-
// authored handoff copy) — this project has no ProjectReference to GenWave.Tts, so they live in
// GenWave.Tts.Tests/Specs/Story243_DjsHandOffAudibly.cs instead. The facts below are wire (PLAN
// T124): a real playout run across a near-term seeded boundary through the production unit loop and
// F74 queue — ceremony airs at track seams, never mid-track (F74.1 stands throughout).
//
// The T120 harness idiom (Story241_StationFollowsTheClock.cs): a real Orchestrator wired to a real
// CachingScheduleResolver/ScheduleResolver/OnAirPersonaAccessor chain, fakes only at the store/tts/
// clock seams, a FakeTimeProvider advanced across the boundary.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureDjsHandOffAudibly
{
    // -------------------------------------------------------------------------
    // Harness — mirrors Story241's BuildProductionChain (the production provider chain wired into a
    // real Orchestrator) plus the two seams T124 adds: CachingScheduleResolver/IPersonaStore feed
    // the handoff producer directly, and a CapturingStationEventSink stands in for the booth log's
    // IStationEventSink (SPEC F92.4).
    // -------------------------------------------------------------------------

    sealed record ProductionChain(
        Orchestrator Orchestrator,
        SpeechDeferralQueue Queue,
        FakeTimeProvider Time,
        FakeScheduleStore ScheduleStore,
        FakeTtsSegmentSource Tts,
        CapturingStationEventSink Events,
        CapturingLogger<Orchestrator> Logger);

    // Every unit's own pull-through step (PullUnitsAsync below): small enough that a fixed count of
    // pulls comfortably brackets a 10-minute F74.3 window either side of a boundary, large enough
    // that the whole run stays fast (no real-time waits — FakeTimeProvider drives every due check).
    static readonly TimeSpan PullStep = TimeSpan.FromSeconds(30);
    const int PullCount = 60; // 30 minutes of simulated wall clock

    static ProductionChain BuildProductionChain(
        FakePersonaStore personaStore, ScheduleWeekSnapshot snapshot, DateTimeOffset now, TimeSpan lookahead,
        CadenceConfig? cadence = null, TimeSpan? renderBudget = null)
    {
        var time = new FakeTimeProvider(now);
        var scheduleStore = new FakeScheduleStore(snapshot);
        var stationDefault = new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault);
        var resolver = new ScheduleResolver(time, stationDefault);
        var caching = new CachingScheduleResolver(scheduleStore, resolver);
        var personaAccessor = new OnAirPersonaAccessor(caching, personaStore, NullLogger<OnAirPersonaAccessor>.Instance);

        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(cadence ?? new CadenceConfig
        {
            LeadInBeforeEachTrack = false,
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits = 0,
        });
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        var logger = new CapturingLogger<Orchestrator>();
        var tts = new FakeTtsSegmentSource();
        var events = new CapturingStationEventSink();
        var queue = new SpeechDeferralQueue(time);
        var catalog = new FakeMediaCatalog(MakeTrackRef("t1"));
        var musicSelectionPolicy = new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance);

        var orchestrator = new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, musicSelectionPolicy,
            tts, personaAccessor, logger,
            new FakeRenderBudgetProvider(renderBudget ?? TimeSpan.FromSeconds(5)),
            queue,
            time, new FakeBoundaryBiasProvider(lookahead),
            scheduleResolver: caching,
            personaStore: personaStore,
            events: events);

        return new ProductionChain(orchestrator, queue, time, scheduleStore, tts, events, logger);
    }

    static Persona MakePersona(long id, string name, string voice)
    {
        var now = DateTime.UnixEpoch;
        return new Persona(id, name, "", "", voice, now, now);
    }

    static MediaReference MakeTrackRef(string id) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: null,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: null,
        Album: null,
        Genre: null,
        Year: null);

    static readonly DayOfWeek Monday = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero).DayOfWeek;

    // Monday 00:00-12:00 = DJ Alpha (persona 10), 12:00-24:00 = DJ Beta (persona 20) — the same
    // two-DJ arrangement Story241 seeds, boundary at noon.
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

    // 5 minutes before the noon boundary — inside a 10-minute F74.3 lookahead window from the very
    // first unit planned.
    static readonly DateTimeOffset JustBeforeNoon = new(2026, 3, 2, 11, 55, 0, TimeSpan.Zero);

    static bool IsSignOff(MediaItem item) =>
        item.MediaId.StartsWith("tts:signoff", StringComparison.OrdinalIgnoreCase);

    static bool IsSignOn(MediaItem item) =>
        item.MediaId.StartsWith("tts:signon", StringComparison.OrdinalIgnoreCase);

    static bool IsMusic(MediaItem item) =>
        !item.MediaId.StartsWith("tts:", StringComparison.Ordinal);

    static async Task<List<MediaItem>> PullUnitsAsync(
        Orchestrator orchestrator, FakeTimeProvider time, TimeSpan step, int count)
    {
        var items = new List<MediaItem>();
        for (var i = 0; i < count; i++)
        {
            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(item);
            items.Add(item);
            time.Advance(step);
        }

        return items;
    }

    static async Task<List<(DateTimeOffset At, MediaItem Item)>> PullUnitsWithTimestampsAsync(
        Orchestrator orchestrator, FakeTimeProvider time, TimeSpan step, int count)
    {
        var pulls = new List<(DateTimeOffset, MediaItem)>();
        for (var i = 0; i < count; i++)
        {
            var at = time.GetUtcNow();
            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(item);
            pulls.Add((at, item));
            time.Advance(step);
        }

        return pulls;
    }

    public sealed class ScenarioCeremonyBracketsTheBoundary
    {
        // Given DJ A's segment ending within the F74.3 lookahead window, When the unit loop plans
        // across the boundary in a real playout run.

        [Fact]
        public async Task SignOffAndSignOnAreEnqueuedFutureDated()
        {
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));
            var noon = new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);

            // The very first unit plan already sits inside the window — arms both pieces.
            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            chain.Time.Advance(TimeSpan.FromMinutes(5)); // now == noon: both due times have elapsed
            var due = chain.Queue.TryDequeueDue(chain.Time.GetUtcNow());

            Assert.Equal(2, due.Count);
            var signOff = Assert.Single(due, d => d.Kind == SpeechDeferralKind.SignOff);
            var signOn = Assert.Single(due, d => d.Kind == SpeechDeferralKind.SignOn);
            Assert.True(signOff.Due > JustBeforeNoon, "sign-off must be future-dated at enqueue time");
            Assert.True(signOn.Due > JustBeforeNoon, "sign-on must be future-dated at enqueue time");
            Assert.True(signOff.Due < signOn.Due, "sign-off is due strictly before sign-on");
            Assert.Equal(noon, signOn.Due);
        }

        [Fact]
        public async Task SignOffAirsAtATrackSeamBeforeTheBoundary()
        {
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            var signOffIndex = items.FindIndex(IsSignOff);
            Assert.True(signOffIndex >= 0, "sign-off never aired");
            var signOnIndex = items.FindIndex(IsSignOn);
            Assert.True(signOnIndex < 0 || signOffIndex < signOnIndex, "sign-off must air before sign-on");
        }

        [Fact]
        public async Task SignOnAirsAtATrackSeamAtTheBoundary()
        {
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            Assert.Contains(items, IsSignOn);
        }

        [Fact]
        public async Task NeitherPieceEverInterruptsATrack()
        {
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            var signOffIndex = items.FindIndex(IsSignOff);
            var signOnIndex = items.FindIndex(IsSignOn);
            Assert.True(signOffIndex >= 0 && signOnIndex >= 0, "both pieces must air for this fact to be meaningful");

            // A whole unit (ceremony piece(s) + the buffered music track they share) is planned
            // atomically before the next track ever reaches air (F74.1) — F92.6's accepted one-unit
            // skew means sign-off and sign-on CAN legitimately drain in the same pass (both already
            // due at the same boundary decision), so the invariant this asserts is the one that
            // actually holds regardless: the LAST ceremony piece is always immediately followed by a
            // plain track, never a third speech segment or a stall.
            var lastCeremonyIndex = Math.Max(signOffIndex, signOnIndex);
            Assert.True(IsMusic(items[lastCeremonyIndex + 1]), "a plain track must follow the ceremony");
        }
    }

    public sealed class ScenarioSeamNeverDoubleArms
    {
        // T124 review findings F2/F4: the producer used to re-arm an already-elapsed SignOff on
        // EVERY unit while a boundary sat in-window — the drain would consume a due SignOff, then
        // the producer (still seeing the SAME BoundaryAt/persona pair, since the resolver's own
        // "current" segment had not yet flipped) would immediately re-enqueue a FRESH SignOff whose
        // due time was already in the past, which the very NEXT unit's drain fired again: an audible
        // double sign-off. Whether a given track's own end (the "seam") lands inside the 15-second
        // SignOffLeadTime window is a harness-alignment question (T124 review finding F4) — shifting
        // the whole 30-second pull grid's start time by a few seconds moves every seam relative to
        // the boundary without changing anything else about the scenario. The 15/20/25s offsets are
        // the ones that used to land a seam inside that window and reproduce the double-air; 0/5/10s
        // cover the other phases so this fact also pins that ordinary seams stay unaffected.

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(15)]
        [InlineData(20)]
        [InlineData(25)]
        public async Task ExactlyOneSignOffAndOneSignOnPerBoundary(int seamOffsetSeconds)
        {
            var start = JustBeforeNoon.AddSeconds(seamOffsetSeconds);
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), start, TimeSpan.FromMinutes(10));

            // PullCount * PullStep is 30 minutes of simulated wall clock from just before noon — the
            // late-drain capture (T124 review finding F4) is explicit here, not incidental: this
            // comfortably runs the loop well past the boundary rather than stopping the instant it is
            // crossed, so a stale re-armed deferral sitting past its own due time has every chance to
            // surface as a second drain.
            await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            Assert.Equal(1, chain.Tts.Requests.Count(r => r.Kind == SegmentKind.SignOff));
            Assert.Equal(1, chain.Tts.Requests.Count(r => r.Kind == SegmentKind.SignOn));
        }
    }

    public sealed class ScenarioRightVoicesRightNames
    {
        // Given the ceremony for A → B, When both pieces render (F92.2). Prompt-content facts (each
        // piece receives the counterpart's display name) live in GenWave.Tts.Tests' own
        // ScenarioRightVoicesRightNames instead (see file header) — these facts cover the
        // Orchestrator's OWN wiring: which voice/name/counterpart lands on the SegmentRequest.

        [Fact]
        public async Task SignOffUsesOutgoingVoiceAndCard()
        {
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            var signOff = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.SignOff);
            Assert.Equal("af_alpha", signOff.Voice);
            Assert.Equal("DJ Alpha", signOff.PersonaName);
            Assert.Equal("DJ Beta", signOff.CounterpartName);
        }

        [Fact]
        public async Task SignOnUsesIncomingVoiceAndCard()
        {
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            var signOn = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.SignOn);
            Assert.Equal("af_beta", signOn.Voice);
            Assert.Equal("DJ Beta", signOn.PersonaName);
            Assert.Equal("DJ Alpha", signOn.CounterpartName);
        }

        [Fact]
        public async Task StationIdentsRemainStationVoiced()
        {
            var cadence = new CadenceConfig
            {
                LeadInBeforeEachTrack = false,
                BackAnnounceAfterEachTrack = false,
                StationIdEveryNUnits = 2,
            };
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10), cadence);

            await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            var stationIds = chain.Tts.Requests.Where(r => r.Kind == SegmentKind.StationId).ToList();
            Assert.NotEmpty(stationIds);
            Assert.All(stationIds, r =>
            {
                Assert.Equal("default", r.Voice); // the station's own identity voice, never a persona's
                Assert.Null(r.PersonaName);
            });
        }
    }

    public sealed class ScenarioMusicOnlyHalves
    {
        // Given boundaries into and out of music-only segments (F92.3).

        static ScheduleWeekSnapshot IntoMusicOnlySchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: null, Genres: ["Ambient"], EnergyMin: null, EnergyMax: null),
        ]);

        static ScheduleWeekSnapshot OutOfMusicOnlySchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: null, Genres: ["Ambient"], EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        // A genuine grid gap (00:00-12:00, nothing scheduled at all) followed by an EXPLICIT
        // persona-less segment (12:00-24:00) — no DJ on either side of the boundary.
        static ScheduleWeekSnapshot GapToMusicOnlySchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: null, Genres: ["Ambient"], EnergyMin: null, EnergyMax: null),
        ]);

        // The SAME persona scheduled on both sides of a row-accurate boundary (the F91.6 seeded
        // grid's own midnight roll, T124 review finding F3 — SPEC F92.3's named self-handoff
        // invariant) — the resolver still reports a real BoundaryAt/NextSegment (it never dedupes
        // a same-persona adjacency), so this producer is the one place "no ceremony airs" is decided.
        static ScheduleWeekSnapshot SamePersonaBothSidesSchedule() => new(
        [
            new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        static FakePersonaStore OneDjStore(long id, string name, string voice)
        {
            var store = new FakePersonaStore();
            store.Add(MakePersona(id, name, voice));
            return store;
        }

        [Fact]
        public async Task IntoMusicOnlyAirsOnlyTheSignOff()
        {
            var chain = BuildProductionChain(
                OneDjStore(10, "DJ Alpha", "af_alpha"), IntoMusicOnlySchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            Assert.Contains(items, IsSignOff);
            Assert.DoesNotContain(items, IsSignOn);
            var signOff = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.SignOff);
            Assert.Null(signOff.CounterpartName);
        }

        [Fact]
        public async Task OutOfMusicOnlyAirsOnlyTheSignOn()
        {
            var chain = BuildProductionChain(
                OneDjStore(20, "DJ Beta", "af_beta"), OutOfMusicOnlySchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            Assert.Contains(items, IsSignOn);
            Assert.DoesNotContain(items, IsSignOff);
            var signOn = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.SignOn);
            Assert.Null(signOn.CounterpartName);
        }

        [Fact]
        public async Task GapToGapBoundaryAirsNothing()
        {
            var chain = BuildProductionChain(
                new FakePersonaStore(), GapToMusicOnlySchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            Assert.DoesNotContain(items, IsSignOff);
            Assert.DoesNotContain(items, IsSignOn);
        }

        [Fact]
        public async Task SamePersonaBoundaryAirsNoCeremonyAtAll()
        {
            var chain = BuildProductionChain(
                OneDjStore(10, "DJ Alpha", "af_alpha"), SamePersonaBothSidesSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            Assert.DoesNotContain(items, IsSignOff);
            Assert.DoesNotContain(items, IsSignOn);
        }

        [Fact]
        public async Task UnresolvableOutgoingPersonaDegradesToSignOnOnly()
        {
            // Persona 10 (outgoing) is named by the schedule row but never added to the store — the
            // "deleted out of band" shape (T124 review finding F3), distinct from
            // IntoMusicOnlyAirsOnlyTheSignOff's "no persona ever scheduled" shape above.
            var chain = BuildProductionChain(
                OneDjStore(20, "DJ Beta", "af_beta"), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            Assert.DoesNotContain(items, IsSignOff);
            Assert.Contains(items, IsSignOn);
            var signOn = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.SignOn);
            Assert.Null(signOn.CounterpartName); // the unresolvable outgoing half names no counterpart
            Assert.Contains(
                chain.Logger.Warnings,
                w => w.Contains("no matching persona row", StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class ScenarioSupersedeProtects
    {
        // Given a pending ceremony and a schedule write that moves the boundary (F92.1).

        [Fact]
        public async Task SupersededPiecesNeverAir()
        {
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));
            var newBoundary = new DateTimeOffset(2026, 3, 2, 12, 10, 0, TimeSpan.Zero);

            // Given a pending ceremony, armed against the noon boundary...
            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.NotNull(chain.Queue.NextDue);

            // ...and a schedule write that moves the boundary ten minutes later (Beta now starts
            // 12:10, not noon) — an admin edit landing while the ceremony sits armed.
            var moved = new ScheduleWeekSnapshot(
            [
                new ScheduleSegment(Id: 1, Day: Monday, StartMinute: 0, EndMinute: 730, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
                new ScheduleSegment(Id: 2, Day: Monday, StartMinute: 730, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
            ]);
            chain.ScheduleStore.SetSnapshot(moved);
            chain.ScheduleStore.RaiseWeekChanged();

            var pulls = await PullUnitsWithTimestampsAsync(chain.Orchestrator, chain.Time, TimeSpan.FromSeconds(30), 120);

            // Nothing airs while still outside the NEW boundary's own window — proves the stale
            // ceremony armed for the OLD (noon) boundary was retracted, not merely superseded late.
            var beforeNewWindowOpens = pulls.Where(p => p.At < newBoundary - TimeSpan.FromMinutes(10)).Select(p => p.Item);
            Assert.DoesNotContain(beforeNewWindowOpens, IsSignOff);
            Assert.DoesNotContain(beforeNewWindowOpens, IsSignOn);

            // ...but a fresh ceremony for the rescheduled boundary still airs later.
            var allItems = pulls.Select(p => p.Item).ToList();
            Assert.Contains(allItems, IsSignOff);
            Assert.Contains(allItems, IsSignOn);
        }
    }

    public sealed class ScenarioBlurbCachePosture
    {
        // Given rendered ceremony pieces (F92.5). Blurb-cache-posture and null-render facts live in
        // GenWave.Tts.Tests' own ScenarioBlurbCachePosture/ScenarioNonLlmAuthoredCopyNeverAirs instead
        // (see file header) — this fact covers the ORCHESTRATOR's own budget integration.

        [Fact]
        public async Task RendersRideThePerUnitBudget()
        {
            var chain = BuildProductionChain(
                TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10),
                renderBudget: TimeSpan.FromMilliseconds(10));
            chain.Tts.RenderDelay = TimeSpan.FromMilliseconds(200); // comfortably exceeds the 10ms budget

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            // Both pieces were ATTEMPTED (rendered, just too slowly)...
            Assert.Contains(chain.Tts.Requests, r => r.Kind == SegmentKind.SignOff);
            Assert.Contains(chain.Tts.Requests, r => r.Kind == SegmentKind.SignOn);
            // ...but neither reached air: the SAME per-unit render budget every other segment kind
            // already rides (SPEC F44.2) dropped both.
            Assert.DoesNotContain(items, IsSignOff);
            Assert.DoesNotContain(items, IsSignOn);
        }
    }

    public sealed class ScenarioFailedPieceDegradesThatBoundaryOnly
    {
        // Sad path — LLM down for a piece render (F92.4): mode, not error.

        [Fact]
        public async Task WhicheverPieceRenderedStillAirs()
        {
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));
            chain.Tts.ShouldReturnNull = req => req.Kind == SegmentKind.SignOff; // the LLM is "down" for just this piece

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            Assert.DoesNotContain(items, IsSignOff);
            Assert.Contains(items, IsSignOn);
        }

        [Fact]
        public async Task BothFailedMeansCleanCutAndMusicNeverWaits()
        {
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));
            chain.Tts.AlwaysReturnNull = true;

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            Assert.DoesNotContain(items, IsSignOff);
            Assert.DoesNotContain(items, IsSignOn);
            // Music never waits on ceremony (F74.1 stands) — every pulled item is a plain track, a
            // clean cut rather than a stall.
            Assert.All(items, i => Assert.True(IsMusic(i)));
        }

        [Fact]
        public async Task DropIsRecordedAsWarnPlusBoothLogEntry()
        {
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));
            chain.Tts.AlwaysReturnNull = true;

            await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            Assert.Contains(chain.Logger.Warnings, w => w.Contains("Handoff piece", StringComparison.OrdinalIgnoreCase));
            var dropped = chain.Events.Events.OfType<HandoffPieceDropped>().ToList();
            Assert.Contains(dropped, d => d.Kind == "SignOff");
            Assert.Contains(dropped, d => d.Kind == "SignOn");
        }

        [Fact]
        public async Task RenderFaultIsRecordedDistinctlyFromANullRender()
        {
            // T124 review finding F6: Task.WhenAny completes as soon as EITHER task finishes, fault
            // or not — it never throws for a faulted renderTask — so classifying the drop cause from
            // "did the render win the race" rather than the completed task's OWN state mislabeled a
            // genuine synth outage as "render returned null". A fault (distinct from
            // ShouldReturnNull's "completed with null") must be recorded as "render faulted".
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));
            chain.Tts.ShouldThrow = req => req.Kind == SegmentKind.SignOff; // the synth genuinely faults for just this piece

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            Assert.DoesNotContain(items, IsSignOff);
            Assert.Contains(items, IsSignOn);
            var dropped = chain.Events.Events.OfType<HandoffPieceDropped>().ToList();
            var signOffDrop = Assert.Single(dropped, d => d.Kind == "SignOff");
            Assert.Equal("render faulted", signOffDrop.Cause);
        }

        [Fact]
        public async Task StationIdDropStaysSilent()
        {
            // T124 review simplify: F92.4's WARN + booth-log-entry treatment is handoff-kind only —
            // every other segment kind's drop stays the pre-existing silent skip. Station-id is the
            // cheapest other kind to prove this with (StationIdEveryNUnits > 0 is all it needs).
            var cadence = new CadenceConfig
            {
                LeadInBeforeEachTrack = false,
                BackAnnounceAfterEachTrack = false,
                StationIdEveryNUnits = 2,
            };
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10), cadence);
            chain.Tts.ShouldReturnNull = req => req.Kind == SegmentKind.StationId; // every station-id render "fails"

            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            Assert.Contains(chain.Tts.Requests, r => r.Kind == SegmentKind.StationId); // attempted...
            Assert.DoesNotContain(
                items, item => item.MediaId.StartsWith("tts:stationid", StringComparison.OrdinalIgnoreCase)); // ...never aired
            Assert.Empty(chain.Events.Events); // F92.4's WARN+booth-log path is handoff-kind only
            Assert.DoesNotContain(chain.Logger.Warnings, w => w.Contains("dropped", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task NextBoundaryAttemptsTheFullCeremonyAgain()
        {
            // A daily-repeating grid (same two DJs every day) so the same structural noon boundary
            // recurs the next day — proves a dropped ceremony at one boundary does not disable the
            // attempt at the NEXT one.
            var schedule = new ScheduleWeekSnapshot(
                Enum.GetValues<DayOfWeek>()
                    .SelectMany(day => new[]
                    {
                        new ScheduleSegment(Id: (long)day * 2 + 1, Day: day, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
                        new ScheduleSegment(Id: (long)day * 2 + 2, Day: day, StartMinute: 720, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
                    })
                    .ToList());
            var chain = BuildProductionChain(TwoDjStore(), schedule, JustBeforeNoon, TimeSpan.FromMinutes(10));
            chain.Tts.AlwaysReturnNull = true; // every ceremony piece drops, at every boundary

            // ~32.5 hours of simulated wall clock — comfortably crosses Monday noon, the Monday/
            // Tuesday midnight roll, AND Tuesday noon.
            await PullUnitsAsync(chain.Orchestrator, chain.Time, TimeSpan.FromMinutes(3), 650);

            var signOffAttempts = chain.Tts.Requests.Count(r => r.Kind == SegmentKind.SignOff);
            Assert.True(
                signOffAttempts >= 2,
                $"expected at least 2 separate sign-off attempts across boundaries, got {signOffAttempts}");
        }
    }
}
