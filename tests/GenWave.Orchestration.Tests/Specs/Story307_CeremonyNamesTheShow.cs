// STORY-307 — Ceremony names the show (F116.2) — boundary/dedupe half
//
// BDD specification — xUnit. Show-aware ceremony lands at T248. The prompt-content half lives in
// GenWave.Tts.Tests/Specs/Story307_ShowCeremonyCopy.cs (the Story243/Story303 split).
//
// The T120 harness idiom (Story241_StationFollowsTheClock.cs, reused verbatim by
// Story243_DjsHandOffAudibly.cs): a real Orchestrator wired to a real CachingScheduleResolver/
// ScheduleResolver/OnAirPersonaAccessor chain, fakes only at the store/tts/clock seams, a
// FakeTimeProvider advanced across the boundary.

namespace GenWave.Orchestration.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

public static class FeatureCeremonyNamesTheShow
{
    // -------------------------------------------------------------------------
    // Harness — Story243_DjsHandOffAudibly.cs's own BuildProductionChain, verbatim.
    // -------------------------------------------------------------------------

    sealed record ProductionChain(
        Orchestrator Orchestrator,
        SpeechDeferralQueue Queue,
        FakeTimeProvider Time,
        FakeScheduleStore ScheduleStore,
        FakeTtsSegmentSource Tts);

    static readonly TimeSpan PullStep = TimeSpan.FromSeconds(30);
    const int PullCount = 60; // 30 minutes of simulated wall clock

    static ProductionChain BuildProductionChain(
        FakePersonaStore personaStore, ScheduleWeekSnapshot snapshot, DateTimeOffset now, TimeSpan lookahead)
    {
        var time = new FakeTimeProvider(now);
        var scheduleStore = new FakeScheduleStore(snapshot);
        var stationDefault = new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault);
        var resolver = new ScheduleResolver(time, stationDefault);
        var caching = new CachingScheduleResolver(scheduleStore, resolver, new FakeScheduleSpecialStore());
        var personaAccessor = new OnAirPersonaAccessor(caching, personaStore, NullLogger<OnAirPersonaAccessor>.Instance);

        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(new CadenceConfig
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
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)),
            queue,
            time, new FakeBoundaryBiasProvider(lookahead),
            scheduleResolver: caching,
            personaStore: personaStore,
            events: events);

        return new ProductionChain(orchestrator, queue, time, scheduleStore, tts);
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

    static FakePersonaStore OneDjStore(long id, string name, string voice)
    {
        var store = new FakePersonaStore();
        store.Add(MakePersona(id, name, voice));
        return store;
    }

    static FakePersonaStore TwoDjStore()
    {
        var store = new FakePersonaStore();
        store.Add(MakePersona(10, "DJ Alpha", "af_alpha"));
        store.Add(MakePersona(20, "DJ Beta", "af_beta"));
        return store;
    }

    static readonly DayOfWeek Monday = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero).DayOfWeek;

    // 5 minutes before the noon boundary — inside a 10-minute F74.3 lookahead window from the very
    // first unit planned (mirrors Story243's own JustBeforeNoon).
    static readonly DateTimeOffset JustBeforeNoon = new(2026, 3, 2, 11, 55, 0, TimeSpan.Zero);

    static readonly ShowSummary MorningShow =
        new(Id: 1, Name: "The Breakfast Show", Tagline: "Mornings with Alpha", Flavor: "upbeat, chatty, coffee-fueled");
    static readonly ShowSummary NightShow =
        new(Id: 2, Name: "Night Moves", Tagline: "Late-night deep cuts", Flavor: "moody, sparse, past midnight");

    static bool IsSignOff(MediaItem item) =>
        item.MediaId.StartsWith("tts:signoff", StringComparison.OrdinalIgnoreCase);

    static bool IsSignOn(MediaItem item) =>
        item.MediaId.StartsWith("tts:signon", StringComparison.OrdinalIgnoreCase);

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

    public sealed class ScenarioSameDjShowFlip
    {
        // Given adjacent blocks: same persona (DJ Alpha), different shows — a boundary that stays
        // row-accurate (F92.3) but is a real boundary for ceremony purposes under F114.3/F116.2.

        static ScheduleWeekSnapshot SameDjDifferentShowSchedule() => new(
        [
            new ScheduleSegment(
                Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10,
                Genres: null, EnergyMin: null, EnergyMax: null, Show: MorningShow, ShowId: MorningShow.Id),
            new ScheduleSegment(
                Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 10,
                Genres: null, EnergyMin: null, EnergyMax: null, Show: NightShow, ShowId: NightShow.Id),
        ]);

        [Fact]
        public async Task ExactlyOneTransitionPieceAirs()
        {
            var chain = BuildProductionChain(
                OneDjStore(10, "DJ Alpha", "af_alpha"), SameDjDifferentShowSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            // When the boundary drains...
            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            // Then exactly ONE ceremony piece airs — the transition-styled sign-on (the F92.4
            // incoming-welcome rung as designed behavior, not a degrade) — and NO sign-off, since
            // there is no other DJ to hand off to.
            Assert.DoesNotContain(items, IsSignOff);
            Assert.Contains(items, IsSignOn);

            var signOn = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.SignOn);
            Assert.Equal("af_alpha", signOn.Voice);
            Assert.Equal("DJ Alpha", signOn.PersonaName);
            Assert.Null(signOn.CounterpartName); // no OTHER DJ — it's the same persona
            Assert.Equal("Night Moves", signOn.ShowName); // the incoming show
            Assert.Equal("moody, sparse, past midnight", signOn.ShowFlavor);
        }
    }

    public sealed class ScenarioDjBoundariesUnchanged
    {
        // Given adjacent blocks with DIFFERENT personas, both shows named — the ordinary F92
        // two-piece ceremony, now additionally show-aware (F116.2 rides every shape, no gate).

        static ScheduleWeekSnapshot TwoDjBothShowsSchedule() => new(
        [
            new ScheduleSegment(
                Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10,
                Genres: null, EnergyMin: null, EnergyMax: null, Show: MorningShow, ShowId: MorningShow.Id),
            new ScheduleSegment(
                Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 20,
                Genres: null, EnergyMin: null, EnergyMax: null, Show: NightShow, ShowId: NightShow.Id),
        ]);

        [Fact]
        public async Task DifferentPersonaBoundariesKeepTheTwoPieceCeremony()
        {
            var chain = BuildProductionChain(TwoDjStore(), TwoDjBothShowsSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            // When the boundary drains...
            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            // Then the F92 two-piece ceremony behaves exactly as shipped: exactly one sign-off AND
            // one sign-on, each naming the OTHER persona same as before F116 — plus, additively, the
            // show fields F116.2 now carries.
            Assert.Contains(items, IsSignOff);
            Assert.Contains(items, IsSignOn);

            var signOff = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.SignOff);
            Assert.Equal("DJ Alpha", signOff.PersonaName);
            Assert.Equal("DJ Beta", signOff.CounterpartName);
            Assert.Equal("The Breakfast Show", signOff.ShowName); // its own (ending) show
            Assert.Equal("Night Moves", signOff.CounterpartShowName); // the next show (F114.3)

            var signOn = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.SignOn);
            Assert.Equal("DJ Beta", signOn.PersonaName);
            Assert.Equal("DJ Alpha", signOn.CounterpartName);
            Assert.Equal("Night Moves", signOn.ShowName); // its own (incoming) show
            Assert.Equal("moody, sparse, past midnight", signOn.ShowFlavor);
        }
    }

    public sealed class ScenarioAmendedDedupe
    {
        // Given adjacent blocks with the SAME persona AND the SAME show.

        static ScheduleWeekSnapshot SamePersonaSameShowSchedule() => new(
        [
            new ScheduleSegment(
                Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: 10,
                Genres: null, EnergyMin: null, EnergyMax: null, Show: MorningShow, ShowId: MorningShow.Id),
            new ScheduleSegment(
                Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: 10,
                Genres: null, EnergyMin: null, EnergyMax: null, Show: MorningShow, ShowId: MorningShow.Id),
        ]);

        [Fact]
        public async Task SamePersonaSameShowStaysSilent()
        {
            var chain = BuildProductionChain(
                OneDjStore(10, "DJ Alpha", "af_alpha"), SamePersonaSameShowSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10));

            // When the boundary passes...
            var items = await PullUnitsAsync(chain.Orchestrator, chain.Time, PullStep, PullCount);

            // Then no ceremony airs at all — F92.3 dedupes on persona AND show (F114.3 as ruled);
            // matching ShowId (same show on both sides) is exactly as silent as the pre-F116
            // showless self-handoff.
            Assert.DoesNotContain(items, IsSignOff);
            Assert.DoesNotContain(items, IsSignOn);
        }
    }
}
