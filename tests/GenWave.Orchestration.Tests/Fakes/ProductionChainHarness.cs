namespace GenWave.Orchestration.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// The T120 harness idiom — a real Orchestrator wired to a real
/// CachingScheduleResolver/ScheduleResolver/OnAirPersonaAccessor chain, fakes only at the
/// store/tts/clock/catalog seams — that Story241_StationFollowsTheClock.cs originated and
/// Story243_DjsHandOffAudibly.cs/Story307_CeremonyNamesTheShow.cs each re-copied nearly verbatim
/// (their own <c>BuildProductionChain</c>, still inline in each file).
///
/// <para>
/// Extracted here at PLAN T250 (review carry-forward from the Story307 build): a 4th verbatim copy
/// for <c>Story309_ShowIdentDrain.cs</c> was ruled the line this repo actually draws — that spec is
/// the one caller built against this shared helper. Migrating the FOUR pre-existing inline copies to
/// it too is optional follow-up (noted, not done here — none of them were touched by this task), so
/// they are named explicitly rather than left to drift silently:
/// <c>Story241_StationFollowsTheClock.cs</c>, <c>Story243_DjsHandOffAudibly.cs</c>, and
/// <c>Story307_CeremonyNamesTheShow.cs</c> each still carry their own near-identical
/// <c>BuildProductionChain</c>; <c>Story303_StraddleHandoff.cs</c> carries a reduced, tuple-returning
/// <c>BuildChain</c> derived from the same idiom. A future change to the wiring shape here (a new
/// Orchestrator constructor param, a new fake seam) will NOT automatically reach any of the four —
/// whoever makes that change should grep for <c>BuildProductionChain</c>/<c>BuildChain</c> across
/// <c>tests/GenWave.Orchestration.Tests/Specs</c> and judge whether it applies there too.
/// </para>
/// </summary>
static class ProductionChainHarness
{
    public sealed record ProductionChain(
        Orchestrator Orchestrator,
        SpeechDeferralQueue Queue,
        FakeTimeProvider Time,
        FakeScheduleStore ScheduleStore,
        FakeTtsSegmentSource Tts,
        CapturingStationEventSink Events,
        CapturingLogger<Orchestrator> Logger,
        FakeMediaCatalog Catalog);

    /// <summary>
    /// Builds one production chain. <paramref name="catalog"/> is the Orchestrator's OWN imaging-pool
    /// catalog (SPEC F110.2/F117.2's <c>GetRandomReadyByImagingKindAsync</c> seam) — a distinct
    /// reference from <see cref="MusicSelectionPolicy"/>'s own catalog dependency, but the SAME
    /// <see cref="FakeMediaCatalog"/> instance backs both here (one script surface, matching how a
    /// real <c>MediaRepository</c> is one object implementing both call shapes) unless a caller wants
    /// them to differ. Defaults to a single-track pool when omitted — every caller that never asserts
    /// on imaging-pool behavior can ignore this parameter entirely. <paramref name="patterEstimator"/>
    /// (gh-#463) is Orchestrator's own patterEstimator seam, passed through unchanged — the default
    /// <see langword="null"/> falls back to Orchestrator's own fresh <c>RollingPatterDurationEstimator</c>,
    /// same as every other caller that never wires one.
    /// </summary>
    public static ProductionChain BuildProductionChain(
        FakePersonaStore personaStore, ScheduleWeekSnapshot snapshot, DateTimeOffset now, TimeSpan lookahead,
        CadenceConfig? cadence = null, TimeSpan? renderBudget = null, FakeMediaCatalog? catalog = null,
        IPatterDurationEstimator? patterEstimator = null, CrosstalkPlanner? crosstalkPlanner = null)
    {
        var time = new FakeTimeProvider(now);
        var scheduleStore = new FakeScheduleStore(snapshot);
        var stationDefault = new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault);
        var resolver = new ScheduleResolver(time, stationDefault);
        var caching = new CachingScheduleResolver(scheduleStore, resolver, new FakeScheduleSpecialStore());
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
        var mediaCatalog = catalog ?? new FakeMediaCatalog(MakeTrackRef("t1"));
        var musicSelectionPolicy = new MusicSelectionPolicy(mediaCatalog, NullLogger<MusicSelectionPolicy>.Instance);

        var orchestrator = new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, musicSelectionPolicy,
            tts, personaAccessor, logger,
            new FakeRenderBudgetProvider(renderBudget ?? TimeSpan.FromSeconds(5)),
            queue,
            time, new FakeBoundaryBiasProvider(lookahead),
            scheduleResolver: caching,
            personaStore: personaStore,
            events: events,
            catalog: mediaCatalog,
            patterEstimator: patterEstimator,
            crosstalkPlanner: crosstalkPlanner);

        return new ProductionChain(orchestrator, queue, time, scheduleStore, tts, events, logger, mediaCatalog);
    }

    public static Persona MakePersona(long id, string name, string voice)
    {
        var now = DateTime.UnixEpoch;
        return new Persona(id, name, "", "", voice, now, now);
    }

    public static MediaReference MakeTrackRef(string id) => new(
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

    public static FakePersonaStore OneDjStore(long id, string name, string voice)
    {
        var store = new FakePersonaStore();
        store.Add(MakePersona(id, name, voice));
        return store;
    }
}
