// STORY-241 — The station follows the clock (SPEC F91.2–F91.5, F91.7, F94.5, PLAN T119/T120)
//
// BDD specification — xUnit, pending. T119 facts exercise ScheduleResolver as a pure
// function (week snapshot + FakeTimeProvider — the DST scenarios pin real tzdata
// transitions). T120 facts are wire: a real playout run over a seeded two-segment
// schedule through the production provider chain, with /api/status covered in the
// Host-side factory idiom.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureStationFollowsTheClock
{
    // -------------------------------------------------------------------------
    // T120 wire helpers — the production provider chain (OnAirPersonaAccessor +
    // ScheduleEnvelopeProvider, both re-backed by ONE shared CachingScheduleResolver) wired into a
    // real Orchestrator, exactly the DI shape StationSettingsHostingExtensions/
    // StationOptionsServiceCollectionExtensions build in the Host. A FakeTimeProvider stands in for
    // the wall clock so a single instance of this chain can be driven across a schedule boundary
    // with no reconstruction — CachingScheduleResolver.Resolve(snapshot) re-derives "now" fresh on
    // every read, so advancing the clock alone is enough to flip every consumer.
    // -------------------------------------------------------------------------

    sealed record ProductionChain(
        Orchestrator Orchestrator,
        OnAirPersonaAccessor PersonaAccessor,
        ScheduleEnvelopeProvider EnvelopeProvider,
        FakeTimeProvider Time,
        CapturingLogger<Orchestrator> Logger,
        FakeTtsSegmentSource Tts);

    static ProductionChain BuildProductionChain(
        FakePersonaStore personaStore, ScheduleWeekSnapshot snapshot, DateTimeOffset now,
        IMediaCatalog catalog, CadenceConfig? cadence = null, IPersonaPickProvider? personaPickProvider = null,
        RotationSettings? rotationSettings = null)
    {
        var time = new FakeTimeProvider(now);
        var scheduleStore = new FakeScheduleStore(snapshot);
        var stationDefault = new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault);
        var resolver = new ScheduleResolver(time, stationDefault);
        var caching = new CachingScheduleResolver(scheduleStore, resolver);
        var personaAccessor = new OnAirPersonaAccessor(caching, personaStore, NullLogger<OnAirPersonaAccessor>.Instance);
        var envelopeProvider = new ScheduleEnvelopeProvider(caching, stationDefault);

        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(cadence ?? new CadenceConfig
        {
            LeadInBeforeEachTrack = false,
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits = 0,
        });
        var rotationProvider = new FakeRotationSettingsProvider(rotationSettings ?? new RotationSettings());
        var logger = new CapturingLogger<Orchestrator>();
        var tts = new FakeTtsSegmentSource();
        var orchestrator = new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog,
            tts, personaAccessor, logger,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)),
            new SpeechDeferralQueue(time),
            time, new FakeBoundaryBiasProvider(TimeSpan.Zero),
            envelopeProvider,
            personaPickProvider);

        return new ProductionChain(orchestrator, personaAccessor, envelopeProvider, time, logger, tts);
    }

    static Persona MakePersona(long id, string name, string voice)
    {
        var now = DateTime.UnixEpoch;
        return new Persona(id, name, "", "", voice, now, now);
    }

    static MediaReference MakeTrackRef(string id, string? genre = null) => new(
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
        Genre: genre,
        Year: null);

    public sealed class ScenarioResolvingTheCurrentSegment
    {
        // Given a stored week snapshot and a station-local wall-clock instant.

        [Fact]
        public void SnapshotCarriesSegmentPersonaEnvelopeBoundaryAndNext()
        {
            // 10:30, inside a fully-specified 09:00-12:00 segment immediately followed by another —
            // proves segment/persona/envelope/boundary/next all come back together in one resolve.
            var now = new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now);
            var day = now.DayOfWeek;

            var onAir = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 540, EndMinute: 720,
                PersonaId: 7, Genres: ["Rock"], EnergyMin: 0.2, EnergyMax: 0.8);
            var upNext = new ScheduleSegment(
                Id: 2, Day: day, StartMinute: 720, EndMinute: 900,
                PersonaId: 9, Genres: null, EnergyMin: null, EnergyMax: null);
            var snapshot = new ScheduleWeekSnapshot([onAir, upNext]);

            var stationDefault = new FakeStationDefaultEnvelopeSource(
                new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["Jazz"], new EnergyRange(0.0, 1.0)));
            var resolver = new ScheduleResolver(time, stationDefault);

            var result = resolver.Resolve(snapshot);

            Assert.Equal(onAir, result.Segment);
            Assert.Equal(7L, result.PersonaId);
            // Compared field-by-field rather than as a whole SegmentEnvelope: the compiler-generated
            // record Equals compares Genres (IReadOnlyList<string>) by reference, not by sequence.
            Assert.Equal(new TimeOnly(9, 0), result.Envelope.StartsAt);
            Assert.Equal(new TimeOnly(12, 0), result.Envelope.EndsAt);
            Assert.Equal(["Rock"], result.Envelope.Genres);
            Assert.Equal(new EnergyRange(0.2, 0.8), result.Envelope.EnergyRange);
            Assert.Equal(new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero), result.BoundaryAt);
            Assert.Equal(upNext, result.NextSegment);
        }

        [Fact]
        public async Task FeederTickPathIssuesNoScheduleStoreQuery()
        {
            var now = new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now);
            var day = now.DayOfWeek;
            var segment = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 0, EndMinute: 1440,
                PersonaId: 3, Genres: null, EnergyMin: null, EnergyMax: null);
            var store = new FakeScheduleStore(new ScheduleWeekSnapshot([segment]));
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));
            var caching = new CachingScheduleResolver(store, resolver);

            for (var tick = 0; tick < 5; tick++)
                await caching.ResolveAsync(CancellationToken.None);

            Assert.Equal(1, store.LoadWeekAsyncCallCount);
        }
    }

    public sealed class ScenarioConsumersFlipAtTheBoundary
    {
        // Given a schedule where DJ A's segment ends and DJ B's begins, When a real
        // playout run crosses the boundary (zero call-site changes — F91.5). Monday 00:00-12:00 =
        // DJ Alpha (persona 10); 12:00-24:00 = DJ Beta (persona 20) — the two-segment schedule the
        // acceptance criteria call for. StatusEndpointReportsBResolverSourced needs the real Host
        // composition root (WebApplicationFactory) — this project cannot see GenWave.Host, so that
        // fact lives in GenWave.Host.Tests instead (Story241_StatusPersonaResolverSourced.cs), per
        // this file's own header note ("/api/status covered in the Host-side factory idiom").

        static readonly DayOfWeek Monday = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero).DayOfWeek;

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
            // Neutral cards (EnergyDisposition 0.0, no taste) — RankerRungZeroObservesB overrides
            // the taste reader per persona; the card itself only needs to exist (RankerPersonaPickProvider
            // short-circuits to null on a card-less persona before ever reaching the catalog).
            store.AddCard(10, MakeNeutralCard("DJ Alpha"));
            store.AddCard(20, MakeNeutralCard("DJ Beta"));
            return store;
        }

        static PersonaCard MakeNeutralCard(string name) =>
            new(PersonaCard.CurrentSchemaVersion, name, "", "", [], new VoiceSpec("kokoro", "", 1.0, "en"),
                EnergyDisposition: 0.0, Lore: [], Corrections: []);

        // 11:59 on the seeded Monday — one minute before the noon boundary into DJ Beta's segment.
        static readonly DateTimeOffset JustBeforeNoon = new(2026, 3, 2, 11, 59, 0, TimeSpan.Zero);

        [Fact]
        public async Task PatterVoiceFlipsFromAToB()
        {
            // One unit = two GetNextAsync calls (LeadIn, then the track itself — GetNextAsync
            // buffers both and dequeues one per call, the real feeder's own pull cadence); the
            // THIRD call is what starts the next unit's pick and renders ITS lead-in.
            var cadence = new CadenceConfig { LeadInBeforeEachTrack = true, BackAnnounceAfterEachTrack = false, StationIdEveryNUnits = 0 };
            var catalog = new FakeMediaCatalog(MakeTrackRef("t1"));
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, catalog, cadence);

            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None); // unit 1: lead-in
            var leadInBefore = chain.Tts.Requests.Single(r => r.Kind == SegmentKind.LeadIn);
            Assert.Equal("af_alpha", leadInBefore.Voice);
            Assert.Equal("DJ Alpha", leadInBefore.PersonaName);

            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None); // unit 1: the track itself

            chain.Time.Advance(TimeSpan.FromMinutes(2)); // crosses noon into DJ Beta's segment

            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None); // unit 2: lead-in
            var leadInAfter = chain.Tts.Requests.Last(r => r.Kind == SegmentKind.LeadIn);
            Assert.Equal("af_beta", leadInAfter.Voice);
            Assert.Equal("DJ Beta", leadInAfter.PersonaName);
        }

        [Fact]
        public async Task BoothLogStampFlipsFromAToB()
        {
            // BoothLogWriter stamps station.booth_log.persona_id from IActivePersonaAccessor's own
            // synchronous ActivePersonaId member (SPEC F84.6) — this proves THAT sync surface itself
            // flips at the boundary, the exact value BoothLogWriter reads at air time; BoothLogWriter's
            // own Publish() logic is unchanged (diff-free consumer) and already covered by Story215.
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, new FakeMediaCatalog(null));
            // Warm-up: TryGetCurrent()'s own boot-window contract (null until SOME caller has
            // resolved at least once) — this stands in for the FIRST per-unit ResolveAsync
            // (Orchestrator.ResolvePersonaAsync/RankerPersonaPickProvider.TryPickAsync), NOT
            // PersonaCardMigrator: that migrator only calls ResolveAsync while ensuring the
            // "default" persona row exists, and short-circuits BEFORE that call once the row is
            // already there (every boot after the very first, in steady state) — so post-restart,
            // the first real pick legitimately reads the station default during this boot window,
            // same as here.
            await chain.PersonaAccessor.ResolveAsync(CancellationToken.None);

            Assert.Equal(10L, chain.PersonaAccessor.ActivePersonaId);

            chain.Time.Advance(TimeSpan.FromMinutes(2));

            Assert.Equal(20L, chain.PersonaAccessor.ActivePersonaId);
        }

        [Fact]
        public async Task NextPersonaNameIsCachedBeforeThatPersonaEverAirs()
        {
            // PLAN T125 review F1 — REAL DEFECT pin, driven through the REAL OnAirPersonaAccessor
            // (fake IPersonaStore, real CachingScheduleResolver/ScheduleResolver), with NO hand-seeding
            // of the name memo: TryGetCachedName only ever populates as a side effect of ResolveAsync
            // succeeding for that SAME id (see OnAirPersonaAccessor's own remarks) — without ALSO
            // warming the NEXT persona off the SAME snapshot, DJ Beta's name would stay uncached for
            // the entire eleven-plus hours DJ Alpha is on air, so upNext.dj would report null
            // ("Nonstop music" to listeners) right up until the boundary Beta actually airs at.
            var chain = BuildProductionChain(TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, new FakeMediaCatalog(null));

            await chain.PersonaAccessor.ResolveAsync(CancellationToken.None); // DJ Alpha's own per-unit resolve

            Assert.Equal(10L, chain.PersonaAccessor.ActivePersonaId); // sanity: Beta has NOT aired yet
            Assert.Equal("DJ Beta", chain.PersonaAccessor.TryGetCachedName(20));
        }

        [Fact]
        public async Task RankerRungZeroObservesB()
        {
            // A real RankerPersonaPickProvider (rung 0) reads the SAME OnAirPersonaAccessor every
            // other consumer reads — each persona's own taste rule strongly favors a different
            // track, so which track wins names which persona the ranker actually saw.
            var alphaRule = new TasteRule(new TastePredicate(Artist: "Artist Alpha", Genre: null, Tag: null),
                new TasteContext(DaysOfWeek: [], StartHour: null, EndHour: null), Weight: 1.0);
            var betaRule = new TasteRule(new TastePredicate(Artist: "Artist Beta", Genre: null, Tag: null),
                new TasteContext(DaysOfWeek: [], StartHour: null, EndHour: null), Weight: 1.0);
            var tasteReader = new FakePersonaScopedTasteReader();
            tasteReader.SetRules(10, [alphaRule]);
            tasteReader.SetRules(20, [betaRule]);

            var pool = new[]
            {
                new EnvelopeCandidateRow(
                    new MediaReference("alpha1", "/media/alpha1.mp3", "Alpha Track", new Loudness(-23.0, -1.0, true),
                        null, null, null, null, "Artist Alpha", null, null, null),
                    Energy: 0.5, Moods: [], RepeatedRecent: false, RepeatedArtist: false),
                new EnvelopeCandidateRow(
                    new MediaReference("beta1", "/media/beta1.mp3", "Beta Track", new Loudness(-23.0, -1.0, true),
                        null, null, null, null, "Artist Beta", null, null, null),
                    Energy: 0.5, Moods: [], RepeatedRecent: false, RepeatedArtist: false),
            };
            var catalog = new FakePersonaPoolCatalog(pool);

            var time = new FakeTimeProvider(JustBeforeNoon);
            var scheduleStore = new FakeScheduleStore(TwoDjSchedule());
            var stationDefault = new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault);
            var resolver = new ScheduleResolver(time, stationDefault);
            var caching = new CachingScheduleResolver(scheduleStore, resolver);
            var personaAccessor = new OnAirPersonaAccessor(caching, TwoDjStore(), NullLogger<OnAirPersonaAccessor>.Instance);

            // Exploration roll (0.99, above the 5% floor) ⇒ not exploration; sample roll (0.0) ⇒
            // picks the highest-scored candidate — whichever rule fired. Two (exploration, sample)
            // pairs: one per TryPickAsync call below (a single provider/ranker instance is reused
            // across the boundary, exactly as the real Orchestrator's own single instance would be).
            var ranker = new PersonaRanker(
                tasteReader, new StubRandomSource(0.99, 0.0, 0.99, 0.0), TimeProvider.System, new PersonaRankerOptions(), NullLogger<PersonaRanker>.Instance);
            var provider = new RankerPersonaPickProvider(catalog, personaAccessor, ranker, new PersonaRankerOptions());

            var envelope = SegmentEnvelope.StationDefault;
            var pickBefore = await provider.TryPickAsync(new LibraryScope([1L]), [], 0, envelope, CancellationToken.None);
            Assert.Equal("alpha1", pickBefore!.Media.MediaId);

            time.Advance(TimeSpan.FromMinutes(2));

            var pickAfter = await provider.TryPickAsync(new LibraryScope([1L]), [], 0, envelope, CancellationToken.None);
            Assert.Equal("beta1", pickAfter!.Media.MediaId);
        }
    }

    public sealed class ScenarioGapsAreStationDefault
    {
        // Given a wall-clock instant covered by no segment.

        static (ScheduleResolver Resolver, SegmentEnvelope StationDefault) BuildGapResolver()
        {
            var now = new DateTimeOffset(2026, 3, 2, 3, 0, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now);
            var stationDefault = new SegmentEnvelope(
                TimeOnly.MinValue, TimeOnly.MaxValue, ["Jazz"], new EnergyRange(0.1, 0.9));
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(stationDefault));
            return (resolver, stationDefault);
        }

        [Fact]
        public void EnvelopeIsStationDefaultValues()
        {
            var (resolver, stationDefault) = BuildGapResolver();

            var result = resolver.Resolve(new ScheduleWeekSnapshot([]));

            Assert.Equal(stationDefault, result.Envelope);
        }

        [Fact]
        public void PersonaIsNone()
        {
            var (resolver, _) = BuildGapResolver();

            var result = resolver.Resolve(new ScheduleWeekSnapshot([]));

            Assert.Null(result.PersonaId);
        }

        [Fact]
        public async Task EmptyGridBehavesAsMusicOnlyStation()
        {
            // A real playout run over an empty grid (F91.4/F91.5) — the pre-clock,
            // no-active-persona, 24/7-music-only behavior, unchanged.
            var now = new DateTimeOffset(2026, 3, 2, 3, 0, 0, TimeSpan.Zero);
            var catalog = new FakeMediaCatalog(MakeTrackRef("t1"));
            var chain = BuildProductionChain(new FakePersonaStore(), new ScheduleWeekSnapshot([]), now, catalog);

            var item = await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("t1", item.MediaId);
            Assert.Null(await chain.PersonaAccessor.ResolveAsync(CancellationToken.None));
            Assert.Null(chain.PersonaAccessor.ActivePersonaId);
            Assert.Equal(SegmentEnvelope.StationDefault, chain.EnvelopeProvider.Current);
            Assert.Equal(IEnvelopeProvider.StationDefaultSentinel, chain.EnvelopeProvider.EnvelopeId);
        }
    }

    public sealed class ScenarioSlotSignsThePick
    {
        // Given a pick made during segment 42 (F91.7). Each fact awaits one warm-up
        // IActivePersonaAccessor.ResolveAsync call before the pick — standing in for the FIRST
        // per-unit ResolveAsync a real deployment's Orchestrator/RankerPersonaPickProvider issues,
        // which is what hydrates CachingScheduleResolver's cached snapshot BEFORE the very first
        // envelope-aware pick ever reads CachingScheduleResolver.TryGetCurrent() synchronously (that
        // method's own documented boot-window contract: null until SOME caller has resolved at
        // least once — see its remarks). NOT PersonaCardMigrator: in steady state (the "default"
        // persona row already exists, i.e. every boot after the very first) it short-circuits
        // before ever calling ResolveAsync, so it primes nothing here.

        [Fact]
        public async Task EnvelopeIdIsSegmentColonId()
        {
            var now = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
            var day = now.DayOfWeek;
            var segment = new ScheduleSegment(
                Id: 42, Day: day, StartMinute: 0, EndMinute: 1440,
                PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null);
            var catalog = new FakeMediaCatalog(MakeTrackRef("t1"));
            var chain = BuildProductionChain(new FakePersonaStore(), new ScheduleWeekSnapshot([segment]), now, catalog);
            await chain.PersonaAccessor.ResolveAsync(CancellationToken.None);

            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var debugLine = Assert.Single(chain.Logger.Entries, e => e.Level == LogLevel.Debug);
            Assert.Contains("envelope=segment:42", debugLine.Message);
        }

        [Fact]
        public async Task GapsUseStationDefaultSentinel()
        {
            var now = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
            var catalog = new FakeMediaCatalog(MakeTrackRef("t1"));
            var chain = BuildProductionChain(new FakePersonaStore(), new ScheduleWeekSnapshot([]), now, catalog);
            await chain.PersonaAccessor.ResolveAsync(CancellationToken.None);

            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var debugLine = Assert.Single(chain.Logger.Entries, e => e.Level == LogLevel.Debug);
            Assert.Contains("envelope=station-default", debugLine.Message);
        }

        [Fact]
        public async Task RelaxationLadderAppliesUnchangedPerSegment()
        {
            // The F81.6 ladder (rotation → energy → genres → terminal) applies exactly as it did
            // against the v1 fixed station-default envelope (Story212) — only the envelope's SOURCE
            // changed (a resolved schedule segment instead of Station:Envelope:*), never the ladder
            // itself.
            var now = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
            var day = now.DayOfWeek;
            var segment = new ScheduleSegment(
                Id: 7, Day: day, StartMinute: 0, EndMinute: 1440,
                PersonaId: null, Genres: ["Rock"], EnergyMin: 0.3, EnergyMax: 0.7);
            var catalog = new FakeLadderMediaCatalog([MakeTrackRef("t1", "Jazz")]);
            var chain = BuildProductionChain(
                new FakePersonaStore(), new ScheduleWeekSnapshot([segment]), now, catalog,
                rotationSettings: new RotationSettings { ArtistSeparation = 2 });
            await chain.PersonaAccessor.ResolveAsync(CancellationToken.None);

            var item = await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("t1", item.MediaId);
            Assert.Equal(4, catalog.EnvelopeCallEnvelopes.Count);
            Assert.Empty(catalog.EnvelopeCallEnvelopes[3].Genres);
            Assert.Contains(chain.Logger.Warnings, w => w.Contains("rotation", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(chain.Logger.Warnings, w => w.Contains("energy band", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(chain.Logger.Warnings, w => w.Contains("genre allow-list to admit", StringComparison.OrdinalIgnoreCase));

            var debugLine = Assert.Single(chain.Logger.Entries, e => e.Level == LogLevel.Debug);
            Assert.Contains("envelope=segment:7", debugLine.Message);
            Assert.Contains("degradation=genres", debugLine.Message);
        }
    }

    public sealed class ScenarioWallClockAcrossDst
    {
        // Given segments spanning the spring-forward and fall-back transitions (F91.2). America/Denver:
        // 2026-03-08 springs forward 02:00->03:00 (the hour never happens); 2026-11-01 falls back
        // 02:00->01:00 (the hour happens twice).

        static TimeZoneInfo DenverZone => TimeZoneInfo.FindSystemTimeZoneById("America/Denver");

        [Fact]
        public void SpringForwardCrossingSegmentAirsOneHourShort()
        {
            // Now = 01:45 MST, inside a 01:30-03:30 wall-clock segment that straddles the gap.
            var now = new DateTimeOffset(2026, 3, 8, 8, 45, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now, DenverZone);
            var day = TimeZoneInfo.ConvertTime(now, DenverZone).DayOfWeek;

            var segment = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 90, EndMinute: 210,
                PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null);
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            var result = resolver.Resolve(new ScheduleWeekSnapshot([segment]));

            // Start (01:30 MST, -07:00) = 08:30 UTC; boundary (03:30 MDT, -06:00) = 09:30 UTC — 60
            // real minutes elapsed for a nominal 120-wall-minute segment.
            var startInstant = new DateTimeOffset(2026, 3, 8, 8, 30, 0, TimeSpan.Zero);
            Assert.Equal(new DateTimeOffset(2026, 3, 8, 9, 30, 0, TimeSpan.Zero), result.BoundaryAt);
            Assert.Equal(TimeSpan.FromHours(1), result.BoundaryAt!.Value - startInstant);
        }

        [Fact]
        public void FallBackCrossingSegmentAirsOneHourLong()
        {
            // Now = 01:45 MDT (the first pass through the repeated hour), inside the same 01:30-03:30
            // wall-clock segment.
            var now = new DateTimeOffset(2026, 11, 1, 7, 45, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now, DenverZone);
            var day = TimeZoneInfo.ConvertTime(now, DenverZone).DayOfWeek;

            var segment = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 90, EndMinute: 210,
                PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null);
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            var result = resolver.Resolve(new ScheduleWeekSnapshot([segment]));

            // Start (01:30, FIRST occurrence, still MDT -06:00) = 07:30 UTC; boundary (03:30, single
            // occurrence, MST -07:00) = 10:30 UTC — 180 real minutes elapsed for the same nominal
            // 120-wall-minute segment.
            var startInstant = new DateTimeOffset(2026, 11, 1, 7, 30, 0, TimeSpan.Zero);
            Assert.Equal(new DateTimeOffset(2026, 11, 1, 10, 30, 0, TimeSpan.Zero), result.BoundaryAt);
            Assert.Equal(TimeSpan.FromHours(3), result.BoundaryAt!.Value - startInstant);
        }

        [Fact]
        public void NonCrossingSegmentsHitTheirWallClockTimesExactly()
        {
            // Same transition day, but a 04:00-05:00 window well clear of the 02:00-03:00 gap — an
            // ordinary hour that must convert exactly, no DST arithmetic involved.
            var now = new DateTimeOffset(2026, 3, 8, 10, 30, 0, TimeSpan.Zero); // 04:30 MDT
            var time = new FakeTimeProvider(now, DenverZone);
            var day = TimeZoneInfo.ConvertTime(now, DenverZone).DayOfWeek;

            var segment = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 240, EndMinute: 300,
                PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null);
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            var result = resolver.Resolve(new ScheduleWeekSnapshot([segment]));

            Assert.Equal(new DateTimeOffset(2026, 3, 8, 11, 0, 0, TimeSpan.Zero), result.BoundaryAt);
        }

        [Fact]
        public void BoundaryLandingInsideSpringForwardGapStepsForwardToFirstValidMinute()
        {
            // Now = 01:00 MST, well before the 02:00->03:00 gap, inside a 00:00-02:30 segment whose
            // BOUNDARY (not just its span) lands on a wall time that never happens — 02:30 is inside the
            // invalid hour and must step FORWARD to 03:00 MDT (T119 review F1a: neither existing DST
            // fact ever resolves an invalid/ambiguous BOUNDARY, only spans that merely cross the gap).
            var now = new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero); // 01:00 MST (-07:00)
            var time = new FakeTimeProvider(now, DenverZone);
            var day = TimeZoneInfo.ConvertTime(now, DenverZone).DayOfWeek;

            var segment = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 0, EndMinute: 150,
                PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null);
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            var result = resolver.Resolve(new ScheduleWeekSnapshot([segment]));

            Assert.Equal(new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero), result.BoundaryAt);
        }

        [Fact]
        public void BoundaryLandingOnAmbiguousFallBackWallTimeResolvesToFirstOccurrence()
        {
            // Now = 00:30 MDT, well before the repeated hour, inside a 00:00-01:30 segment whose
            // BOUNDARY (01:30) is itself the ambiguous wall time — must resolve to the FIRST (still-MDT)
            // occurrence (T119 review F1b). Pins the Max()-not-Min() choice: Min() would give 08:30Z.
            var now = new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero); // 00:30 MDT (-06:00)
            var time = new FakeTimeProvider(now, DenverZone);
            var day = TimeZoneInfo.ConvertTime(now, DenverZone).DayOfWeek;

            var segment = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 0, EndMinute: 90,
                PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null);
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            var result = resolver.Resolve(new ScheduleWeekSnapshot([segment]));

            Assert.Equal(new DateTimeOffset(2026, 11, 1, 7, 30, 0, TimeSpan.Zero), result.BoundaryAt);
        }

        [Fact]
        public void BoundaryAmbiguousDuringItsOwnSecondPassResolvesToTheSecondOccurrence()
        {
            // REAL DEFECT pin (T119 review F2): now = 01:10 MST (-07:00) — the SECOND pass through the
            // repeated hour — inside a 00:00-01:30 segment whose boundary (01:30) is that same ambiguous
            // minute. The FIRST occurrence (07:30Z) has already elapsed by now (08:10Z); resolving to it
            // would hand back a boundary already 40 minutes in the past, violating OnAirSnapshot's "next
            // instant" contract. Must resolve to the SECOND occurrence instead.
            var now = new DateTimeOffset(2026, 11, 1, 8, 10, 0, TimeSpan.Zero); // 01:10 MST, second pass
            var time = new FakeTimeProvider(now, DenverZone);
            var day = TimeZoneInfo.ConvertTime(now, DenverZone).DayOfWeek;

            var segment = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 0, EndMinute: 90,
                PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null);
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            var result = resolver.Resolve(new ScheduleWeekSnapshot([segment]));

            Assert.Equal(new DateTimeOffset(2026, 11, 1, 8, 30, 0, TimeSpan.Zero), result.BoundaryAt);
            Assert.True(result.BoundaryAt > now, "a resolved boundary must never already be in the past");
        }
    }

    public sealed class ScenarioBoundaryAndNextStayRowAccurate
    {
        // Given the F92.3 ruling recorded on OnAirSnapshot.BoundaryAt/NextSegment: same-persona
        // adjacency is never deduped by the resolver — only the ceremony producer (T124) decides that.

        [Fact]
        public void MidnightWrapOnSeededSinglePersonaGridReportsSameDjRowBoundary()
        {
            // F91.6 seeded state: an ActiveId-only upgrade seeds seven all-day rows, one per weekday,
            // all the same persona. Saturday 23:30 must still wrap to Sunday 00:00 local and report
            // Sunday's row as NextSegment — even though outgoing and incoming persona are identical —
            // per the F92.3 ruling ("the resolver keeps BoundaryAt/NextSegment row-accurate ... The
            // F91.6 seeded grid ... must never produce a midnight self-handoff" is the CEREMONY
            // producer's job, not this type's).
            var now = new DateTimeOffset(2026, 8, 1, 23, 30, 0, TimeSpan.Zero); // Saturday 23:30 UTC
            var time = new FakeTimeProvider(now);
            const long personaId = 42;
            var segments = Enum.GetValues<DayOfWeek>()
                .Select(day => new ScheduleSegment(
                    Id: (long)day + 1, Day: day, StartMinute: 0, EndMinute: 1440,
                    PersonaId: personaId, Genres: null, EnergyMin: null, EnergyMax: null))
                .ToList();
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            var result = resolver.Resolve(new ScheduleWeekSnapshot(segments));

            Assert.Equal(new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero), result.BoundaryAt);
            Assert.Equal(DayOfWeek.Sunday, result.NextSegment!.Day);
            Assert.Equal(personaId, result.NextSegment.PersonaId);
            Assert.Equal(personaId, result.PersonaId);
        }

        [Fact]
        public void GapFollowingCurrentSegmentReportsNullNextSegment()
        {
            // A single segment with no adjacent row at its own boundary — the boundary is followed by a
            // grid gap, so NextSegment must be null even though BoundaryAt is still reported.
            var now = new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now);
            var day = now.DayOfWeek;

            var segment = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 540, EndMinute: 720,
                PersonaId: 7, Genres: null, EnergyMin: null, EnergyMax: null);
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            var result = resolver.Resolve(new ScheduleWeekSnapshot([segment]));

            Assert.Equal(new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero), result.BoundaryAt);
            Assert.Null(result.NextSegment);
        }
    }

    public sealed class ScenarioCacheReloadsAfterMidFlightInvalidation
    {
        // REAL DEFECT pin (T119 review F4): a WeekChanged invalidation firing WHILE a load is already
        // in flight must not be lost — the resolver must reload again on a SUBSEQUENT call rather than
        // serving the pre-invalidation snapshot forever.

        [Fact]
        public async Task InvalidationDuringInFlightLoadForcesReloadOnNextResolve()
        {
            var now = new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(now);
            var day = now.DayOfWeek;
            var staleSegment = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 0, EndMinute: 1440,
                PersonaId: 1, Genres: null, EnergyMin: null, EnergyMax: null);
            var freshSegment = new ScheduleSegment(
                Id: 2, Day: day, StartMinute: 0, EndMinute: 1440,
                PersonaId: 2, Genres: null, EnergyMin: null, EnergyMax: null);
            var store = new FakeScheduleStore(new ScheduleWeekSnapshot([staleSegment]));
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));
            var caching = new CachingScheduleResolver(store, resolver);

            store.ArmGate();
            var resolveTask = caching.ResolveAsync(CancellationToken.None);
            store.RaiseWeekChanged(); // fires WHILE the first load is still in flight
            store.SetSnapshot(new ScheduleWeekSnapshot([freshSegment])); // the write that triggered it
            store.ReleaseGate(new ScheduleWeekSnapshot([staleSegment])); // this in-flight read started before the write
            var first = await resolveTask;

            var second = await caching.ResolveAsync(CancellationToken.None);

            Assert.Equal(1L, first.PersonaId); // acceptable: the read in flight when the write landed
            Assert.Equal(2L, second.PersonaId); // the mid-flight invalidation was NOT lost — reloaded
            Assert.Equal(2, store.LoadWeekAsyncCallCount);
        }
    }

    public sealed class ScenarioStalePersonaDegrades
    {
        // Sad path — a schedule row whose persona was deleted out-of-band (F91.5). Exercises
        // OnAirPersonaAccessor directly — the resolver-backed replacement for the retired
        // Station:Persona:ActiveId-reading accessor, same never-throws/WarnOnce contract (F35.5).

        static (OnAirPersonaAccessor Accessor, FakePersonaStore Store, CapturingLogger<OnAirPersonaAccessor> Logger)
            BuildAccessor(long personaId, DateTimeOffset now)
        {
            var day = now.DayOfWeek;
            var segment = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 0, EndMinute: 1440,
                PersonaId: personaId, Genres: null, EnergyMin: null, EnergyMax: null);
            var time = new FakeTimeProvider(now);
            var store = new FakeScheduleStore(new ScheduleWeekSnapshot([segment]));
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));
            var caching = new CachingScheduleResolver(store, resolver);
            var personaStore = new FakePersonaStore();
            var logger = new CapturingLogger<OnAirPersonaAccessor>();
            var accessor = new OnAirPersonaAccessor(caching, personaStore, logger);
            return (accessor, personaStore, logger);
        }

        [Fact]
        public async Task SegmentBehavesPersonaLessWithWarnOnce()
        {
            // Persona 99 is named by the segment but has no matching row (deleted out of band) —
            // degrades to persona-less with exactly ONE warn across repeated resolves of the SAME
            // stale id (mirrors the retired accessor's own lastWarnedActiveId dedup).
            var now = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
            var (accessor, _, logger) = BuildAccessor(99, now);

            var first = await accessor.ResolveAsync(CancellationToken.None);
            var second = await accessor.ResolveAsync(CancellationToken.None);

            Assert.Null(first);
            Assert.Null(second);
            Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task ResolverNeverThrows()
        {
            // A store fault degrades the same way a missing row does (F12.4) — the render path
            // this seam feeds must always get an answer, never a stall/throw.
            var now = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
            var (accessor, store, logger) = BuildAccessor(7, now);
            store.ThrowOnGetById = new InvalidOperationException("store boom (test double)");

            var persona = await accessor.ResolveAsync(CancellationToken.None);

            Assert.Null(persona);
            Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task ScheduleStoreLoadFaultDegradesWithWarnOnceAndRecovers()
        {
            // PLAN T120 review F3: IScheduleStore.LoadWeekAsync (not the persona-row lookup above)
            // faults — CachingScheduleResolver.ResolveAsync propagates it, and
            // OnAirPersonaAccessor.TryResolveOnAirAsync's OWN never-throws/scheduleFaultWarned latch
            // (distinct from ResolveAsync's lastWarnedPersonaId dedup — a schedule-load fault names no
            // persona id to key off) degrades to persona-less with exactly one warn across repeated
            // faults, then resets the moment a resolve succeeds again so a LATER, genuinely new
            // outage still gets its own warn.
            var now = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
            var day = now.DayOfWeek;
            var segment = new ScheduleSegment(
                Id: 1, Day: day, StartMinute: 0, EndMinute: 1440,
                PersonaId: 7, Genres: null, EnergyMin: null, EnergyMax: null);
            var time = new FakeTimeProvider(now);
            var scheduleStore = new FakeScheduleStore(new ScheduleWeekSnapshot([segment]));
            var resolver = new ScheduleResolver(time, new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));
            var caching = new CachingScheduleResolver(scheduleStore, resolver);
            var personaStore = new FakePersonaStore();
            personaStore.Add(MakePersona(7, "DJ Fault", "af_fault"));
            var logger = new CapturingLogger<OnAirPersonaAccessor>();
            var accessor = new OnAirPersonaAccessor(caching, personaStore, logger);

            scheduleStore.ThrowOnLoadWeek = new InvalidOperationException("schedule store boom (test double)");

            var first = await accessor.ResolveAsync(CancellationToken.None);
            var second = await accessor.ResolveAsync(CancellationToken.None);

            Assert.Null(first);
            Assert.Null(second);
            Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning); // latch: one warn across two faults

            scheduleStore.ThrowOnLoadWeek = null; // the fault clears
            var recovered = await accessor.ResolveAsync(CancellationToken.None);
            Assert.Equal("DJ Fault", recovered!.Name);
            Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning); // recovery itself logs nothing new

            // CachingScheduleResolver only re-queries the store on a WeekChanged invalidation (that is
            // its whole point — see its own remarks) — the successful load above already cleaned the
            // cache, so a LATER outage only reaches LoadWeekAsync again once something invalidates it.
            scheduleStore.RaiseWeekChanged();
            scheduleStore.ThrowOnLoadWeek = new InvalidOperationException("a later, genuinely new outage");
            await accessor.ResolveAsync(CancellationToken.None);
            Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning)); // latch reset — the new outage warns again
        }

        [Fact]
        public async Task ResolveCardAsyncDegradesOnCardFaultWithWarnOnce()
        {
            // PLAN T120 review F3(b): ResolveCardAsync's OWN degrade path
            // (lastWarnedCardPersonaId) — distinct from ResolveAsync's lastWarnedPersonaId above,
            // deliberately never sharing the dedup field (RankerPersonaPickProvider calls both
            // members per pick and each fault deserves its own single warn).
            var now = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
            var (accessor, store, logger) = BuildAccessor(11, now);
            store.Add(MakePersona(11, "DJ Cardless", "af_cardless"));
            store.ThrowOnGetCardById = new InvalidOperationException("card store boom (test double)");

            var first = await accessor.ResolveCardAsync(CancellationToken.None);
            var second = await accessor.ResolveCardAsync(CancellationToken.None);

            Assert.Null(first);
            Assert.Null(second);
            Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        }
    }
}
