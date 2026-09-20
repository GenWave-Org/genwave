using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration;
using GenWave.TestSupport.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.TestSupport;

/// <summary>
/// Builds a real <see cref="Orchestrator"/> one seam at a time (SPEC F184.2, STORY-451, PLAN T511) —
/// every seam defaults to the same fake <see cref="ProductionChainHarness"/> already uses, and every
/// seam has a <c>With*</c> override, so a spec never reaches for the 19-parameter constructor
/// directly. <see cref="Build"/> resolves seams whose default depends on another seam's FINAL value
/// (the clock feeds tts/deferral-queue/schedule-resolver; the catalog feeds the music selection
/// policy; the schedule resolver and persona store feed the persona accessor) in dependency order, so
/// <c>With*</c> calls in any order before <see cref="Build"/> always produce a consistent chain.
///
/// <para>
/// <see cref="OrchestratorChain"/> types every slot to the seam type its <c>With*</c> accepts, so
/// <see cref="Build"/> never throws for an override that compiles. A spec that needs a fake's own
/// members on a default seam passes its own fake through <c>With*</c> and keeps the reference itself.
/// </para>
///
/// <para>
/// <see cref="PatterEstimator"/> defaults to <see langword="null"/>, not a fake: the only
/// <c>IPatterDurationEstimator</c> double here (<see cref="CapturingPatterDurationEstimator"/>) always
/// answers a fixed heuristic, which is NOT behaviorally equivalent to <see cref="Orchestrator"/>'s own
/// tiered fallback — defaulting to it would silently change boundary-fit behavior for every spec that
/// never calls <see cref="WithPatterEstimator"/>.
/// </para>
/// </summary>
public sealed class OrchestratorBuilder
{
    IStationIdentityProvider? identityProvider;
    IStationScopeProvider? scopeProvider;
    ICadenceProvider? cadenceProvider;
    IRotationSettingsProvider? rotationProvider;
    MusicSelectionPolicy? musicSelectionPolicy;
    ITtsSegmentSource? tts;
    IActivePersonaAccessor? personaAccessor;
    ILogger<Orchestrator>? logger;
    IRenderBudgetProvider? renderBudgetProvider;
    SpeechDeferralQueue? deferralQueue;
    TimeProvider? timeProvider;
    IBoundaryBiasProvider? boundaryBiasProvider;
    CachingScheduleResolver? scheduleResolver;
    IPersonaStore? personaStore;
    IStationEventSink? events;
    IStationClockProvider? stationClock;
    IPatterDurationEstimator? patterEstimator;
    IContextSettingsProvider? contextSettings;
    IMediaCatalog? catalog;
    IStationImagingSettingsProvider? imagingSettings;
    CrosstalkPlanner? crosstalkPlanner;
    IAnnouncementSource? announcementSource;
    IVerbatimSegmentRenderer? announcementRenderer;
    ITtsVoiceLister? voiceLister;
    IAnnouncementCopyWriter? announcementCopyWriter;
    IAdCadenceProvider? adCadenceProvider;
    IAdSpotVend? adSpotVend;
    IBreakPlanObserver? planObserver;
    ISpeakerSnapshotSource? speakerSnapshotSource;

    // Only meaningful when scheduleResolver is unset — feeds the default schedule-resolver chain's
    // FakeScheduleStore (see WithSchedule).
    ScheduleWeekSnapshot snapshot = new([]);
    bool scheduleSet;

    /// <summary>Every trivial "set the seam, return this" setter below funnels through here — one
    /// place owns the assign-and-chain boilerplate instead of 27 near-identical bodies.</summary>
    OrchestratorBuilder With<T>(ref T? slot, T? value) where T : class
    {
        slot = value;
        return this;
    }

    /// <summary>Overrides the station identity seam. Defaults to a fixed <c>FakeStationIdentityProvider</c>.</summary>
    public OrchestratorBuilder WithIdentity(IStationIdentityProvider identity) => With(ref identityProvider, identity);

    /// <summary>Overrides the library scope seam. Defaults to a fixed <c>FakeStationScopeProvider</c>.</summary>
    public OrchestratorBuilder WithScope(IStationScopeProvider scope) => With(ref scopeProvider, scope);

    /// <summary>Overrides the cadence seam. Defaults to a <c>FakeCadenceProvider</c> with every knob off.</summary>
    public OrchestratorBuilder WithCadence(ICadenceProvider cadence) => With(ref cadenceProvider, cadence);

    /// <summary>Convenience — wraps <paramref name="cadence"/> in a <see cref="FakeCadenceProvider"/>.</summary>
    public OrchestratorBuilder WithCadence(CadenceConfig cadence) => WithCadence(new FakeCadenceProvider(cadence));

    /// <summary>Overrides the rotation settings seam. Defaults to a fixed <c>FakeRotationSettingsProvider</c>.</summary>
    public OrchestratorBuilder WithRotation(IRotationSettingsProvider rotation) => With(ref rotationProvider, rotation);

    /// <summary>Overrides the music selection policy seam. Defaults to a real policy over the final catalog.</summary>
    public OrchestratorBuilder WithMusicSelectionPolicy(MusicSelectionPolicy policy) => With(ref musicSelectionPolicy, policy);

    /// <summary>Overrides the TTS seam. Defaults to a <see cref="FakeTtsSegmentSource"/> sharing the final clock.</summary>
    public OrchestratorBuilder WithTts(ITtsSegmentSource ttsSegmentSource) => With(ref tts, ttsSegmentSource);

    /// <summary>Overrides the active-persona accessor seam. Defaults to a real accessor over the final schedule resolver and persona store.</summary>
    public OrchestratorBuilder WithPersonaAccessor(IActivePersonaAccessor accessor) => With(ref personaAccessor, accessor);

    /// <summary>Overrides the logger seam. Defaults to a <see cref="CapturingLogger{T}"/>.</summary>
    public OrchestratorBuilder WithLogger(ILogger<Orchestrator> orchestratorLogger) => With(ref logger, orchestratorLogger);

    /// <summary>Overrides the render budget seam. Defaults to a fixed 5-second <c>FakeRenderBudgetProvider</c>.</summary>
    public OrchestratorBuilder WithRenderBudget(IRenderBudgetProvider renderBudget) => With(ref renderBudgetProvider, renderBudget);

    /// <summary>Convenience — wraps <paramref name="budget"/> in a <see cref="FakeRenderBudgetProvider"/>.</summary>
    public OrchestratorBuilder WithRenderBudget(TimeSpan budget) => WithRenderBudget(new FakeRenderBudgetProvider(budget));

    /// <summary>Overrides the speech deferral queue seam. Defaults to a queue over the final clock.</summary>
    public OrchestratorBuilder WithDeferralQueue(SpeechDeferralQueue queue) => With(ref deferralQueue, queue);

    /// <summary>Overrides the clock seam. Defaults to a <see cref="FakeTimeProvider"/> at a fixed instant.</summary>
    public OrchestratorBuilder WithTime(TimeProvider time) => With(ref timeProvider, time);

    /// <summary>Convenience — a <see cref="FakeTimeProvider"/> at <paramref name="now"/>.</summary>
    public OrchestratorBuilder WithNow(DateTimeOffset now) => WithTime(new FakeTimeProvider(now));

    /// <summary>Overrides the boundary bias seam. Defaults to a zero-lookahead <c>FakeBoundaryBiasProvider</c>.</summary>
    public OrchestratorBuilder WithBoundaryBias(IBoundaryBiasProvider boundaryBias) => With(ref boundaryBiasProvider, boundaryBias);

    /// <summary>Convenience — wraps <paramref name="lookahead"/> in a <see cref="FakeBoundaryBiasProvider"/>.</summary>
    public OrchestratorBuilder WithLookahead(TimeSpan lookahead) => WithBoundaryBias(new FakeBoundaryBiasProvider(lookahead));

    /// <summary>Overrides the schedule resolver seam outright. Exclusive with <see cref="WithSchedule"/> — <see cref="Build"/> throws if both are set.</summary>
    public OrchestratorBuilder WithScheduleResolver(CachingScheduleResolver resolver) => With(ref scheduleResolver, resolver);

    /// <summary>Convenience — keeps the default schedule-resolver chain but seeds its <see cref="FakeScheduleStore"/> with <paramref name="week"/>. Exclusive with <see cref="WithScheduleResolver"/> — <see cref="Build"/> throws if both are set.</summary>
    public OrchestratorBuilder WithSchedule(ScheduleWeekSnapshot week)
    {
        snapshot = week;
        scheduleSet = true;
        return this;
    }

    /// <summary>Overrides the persona store seam. Defaults to an empty <see cref="FakePersonaStore"/>.</summary>
    public OrchestratorBuilder WithPersonaStore(IPersonaStore store) => With(ref personaStore, store);

    /// <summary>Overrides the station event sink seam. Defaults to a <see cref="CapturingStationEventSink"/>.</summary>
    public OrchestratorBuilder WithEvents(IStationEventSink sink) => With(ref events, sink);

    /// <summary>Overrides the station clock seam. Defaults to <see langword="null"/> (feature-dark — Orchestrator falls back to <see cref="TimeProvider"/>'s own UTC now).</summary>
    public OrchestratorBuilder WithStationClock(IStationClockProvider? clock) => With(ref stationClock, clock);

    /// <summary>Overrides the patter duration estimator seam. Defaults to <see langword="null"/> — see this class's own remarks for why the builder never defaults this to a fake.</summary>
    public OrchestratorBuilder WithPatterEstimator(IPatterDurationEstimator? estimator) => With(ref patterEstimator, estimator);

    /// <summary>Overrides the per-key context-persona seam. Defaults to <see langword="null"/> (feature-dark).</summary>
    public OrchestratorBuilder WithContextSettings(IContextSettingsProvider? settings) => With(ref contextSettings, settings);

    /// <summary>Overrides the media catalog seam. Defaults to a single-track <see cref="FakeMediaCatalog"/> (also feeds the default <see cref="MusicSelectionPolicy"/>).</summary>
    public OrchestratorBuilder WithCatalog(IMediaCatalog mediaCatalog) => With(ref catalog, mediaCatalog);

    /// <summary>Overrides the station imaging settings seam. Defaults to <see langword="null"/> (feature-dark).</summary>
    public OrchestratorBuilder WithImagingSettings(IStationImagingSettingsProvider? settings) => With(ref imagingSettings, settings);

    /// <summary>Overrides the crosstalk planner seam. Defaults to <see langword="null"/> (feature-dark).</summary>
    public OrchestratorBuilder WithCrosstalkPlanner(CrosstalkPlanner? planner) => With(ref crosstalkPlanner, planner);

    /// <summary>Overrides the announcement source seam. Defaults to <see langword="null"/> (feature-dark).</summary>
    public OrchestratorBuilder WithAnnouncementSource(IAnnouncementSource? source) => With(ref announcementSource, source);

    /// <summary>Overrides the verbatim announcement renderer seam. Defaults to <see langword="null"/> (feature-dark).</summary>
    public OrchestratorBuilder WithAnnouncementRenderer(IVerbatimSegmentRenderer? renderer) => With(ref announcementRenderer, renderer);

    /// <summary>Overrides the TTS voice lister seam. Defaults to <see langword="null"/> (feature-dark).</summary>
    public OrchestratorBuilder WithVoiceLister(ITtsVoiceLister? lister) => With(ref voiceLister, lister);

    /// <summary>Overrides the announcement copy writer seam. Defaults to <see langword="null"/> (feature-dark).</summary>
    public OrchestratorBuilder WithAnnouncementCopyWriter(IAnnouncementCopyWriter? writer) => With(ref announcementCopyWriter, writer);

    /// <summary>Overrides the ad cadence seam. Defaults to <see langword="null"/> (feature-dark — no ad ever fires).</summary>
    public OrchestratorBuilder WithAdCadence(IAdCadenceProvider? adCadence) => With(ref adCadenceProvider, adCadence);

    /// <summary>Overrides the ad spot vend seam. Defaults to <see langword="null"/> (feature-dark — no ad ever fires).</summary>
    public OrchestratorBuilder WithAdSpotVend(IAdSpotVend? vend) => With(ref adSpotVend, vend);

    /// <summary>
    /// PLAN T522 — overrides the <see cref="IBreakPlanObserver"/> seam on the shared
    /// <see cref="BreakPlanner"/> <see cref="Build"/> constructs. Defaults to
    /// <see cref="NoOpBreakPlanObserver.Instance"/>; a spec that wants to see every unit's own
    /// <c>BreakPlan</c> (e.g. its trace) passes a <c>CapturingBreakPlanObserver</c> here.
    /// </summary>
    public OrchestratorBuilder WithPlanObserver(IBreakPlanObserver? observer) => With(ref planObserver, observer);

    /// <summary>Overrides the speaker snapshot source seam (PLAN T527, SPEC F188.4). Defaults to <see langword="null"/> (feature-dark — no slot's request is ever stamped, SPEC F189.6). Wired into BOTH the shared <see cref="BreakPlanner"/> and the <see cref="Orchestrator"/> itself <see cref="Build"/> constructs.</summary>
    public OrchestratorBuilder WithSpeakerSnapshotSource(ISpeakerSnapshotSource? source) => With(ref speakerSnapshotSource, source);

    /// <summary>
    /// Resolves every unset seam to its default and constructs the Orchestrator, returning it alongside
    /// every collaborator a spec has ever needed to assert on directly.
    /// </summary>
    public OrchestratorChain Build()
    {
        if (scheduleResolver is not null && scheduleSet)
        {
            throw new InvalidOperationException("WithSchedule and WithScheduleResolver are exclusive");
        }

        var resolvedTime = timeProvider ?? new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var resolvedCatalog = catalog ?? new FakeMediaCatalog(TestData.MakeTrackRef("t1"));

        var resolvedMusicSelectionPolicy = musicSelectionPolicy
            ?? new MusicSelectionPolicy(resolvedCatalog, NullLogger<MusicSelectionPolicy>.Instance);

        var resolvedPersonaStore = personaStore ?? new FakePersonaStore();

        var resolvedScheduleResolver = scheduleResolver;
        if (resolvedScheduleResolver is null)
        {
            var scheduleStore = new FakeScheduleStore(snapshot);
            var stationDefault = new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault);
            var resolver = new ScheduleResolver(resolvedTime, stationDefault);
            resolvedScheduleResolver = new CachingScheduleResolver(scheduleStore, resolver, new FakeScheduleSpecialStore());
        }

        var resolvedPersonaAccessor = personaAccessor
            ?? new OnAirPersonaAccessor(resolvedScheduleResolver, resolvedPersonaStore, NullLogger<OnAirPersonaAccessor>.Instance);

        // The render double's own RenderDelay rides the SAME fake clock as the render budget (STORY-442,
        // PLAN T483) — mirrors ProductionChainHarness's own wiring.
        var resolvedTts = tts ?? new FakeTtsSegmentSource { TimeProvider = resolvedTime };

        var resolvedEvents = events ?? new CapturingStationEventSink();

        var resolvedLogger = logger ?? new CapturingLogger<Orchestrator>();

        var resolvedDeferralQueue = deferralQueue ?? new SpeechDeferralQueue(resolvedTime);

        var resolvedIdentityProvider = identityProvider
            ?? new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var resolvedScopeProvider = scopeProvider ?? new FakeStationScopeProvider(new LibraryScope([1L]));
        var resolvedCadenceProvider = cadenceProvider ?? new FakeCadenceProvider(new CadenceConfig
        {
            LeadInBeforeEachTrack = false,
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits = 0,
        });
        var resolvedRotationProvider = rotationProvider ?? new FakeRotationSettingsProvider(new RotationSettings());
        var resolvedRenderBudgetProvider = renderBudgetProvider ?? new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5));
        var resolvedBoundaryBiasProvider = boundaryBiasProvider ?? new FakeBoundaryBiasProvider(TimeSpan.Zero);

        // PLAN T522 (SPEC F188) — the shared plan-phase seam, built from the SAME resolved locals the
        // Orchestrator itself is about to take below, so both read the identical instances. The
        // announcementSource/announcementRenderer double-seam gate (Orchestrator.cs's own pre-T522
        // step 1.75) now lives HERE, at this construction site — see BreakPlanner's own remarks for
        // why it cannot reproduce that gate itself.
        //
        // BreakPlanner's logger is a ForwardingLogger onto resolvedLogger, not a NullLogger: pre-T522,
        // every one of BreakPlanner's own WARN/INFO lines (e.g. LogTimeDateExpiry) was logged directly
        // through Orchestrator's own logger field, and STORY-452's frozen replay asserts against
        // exactly that logger. Production reaches the same result via DI (both categories resolve from
        // the same ILoggerFactory); this reproduces it for the single CapturingLogger a spec holds.
        var resolvedPlanner = new BreakPlanner(
            resolvedPersonaAccessor,
            new ForwardingLogger<BreakPlanner>(resolvedLogger),
            resolvedRenderBudgetProvider,
            resolvedDeferralQueue,
            resolvedTime,
            resolvedScopeProvider,
            stationClock: stationClock,
            scheduleResolver: resolvedScheduleResolver,
            contextSettings: contextSettings,
            catalog: resolvedCatalog,
            imagingSettings: imagingSettings,
            crosstalkPlanner: crosstalkPlanner,
            announcementSource: announcementRenderer is not null ? announcementSource : null,
            voiceLister: voiceLister,
            adCadenceProvider: adCadenceProvider,
            adSpotVend: adSpotVend,
            personaStore: resolvedPersonaStore,
            speakerSnapshots: speakerSnapshotSource);

        // PLAN T532 (SPEC F190) — the handoff ceremony producer, built from the SAME resolved locals
        // BreakPlanner just took above, mirroring that construction's own ForwardingLogger idiom so
        // this producer's own WARN/INFO lands on the SAME CapturingLogger a spec already asserts
        // against via WithLogger/OrchestratorChain.Logger.
        var resolvedHandoffCeremonyProducer = new HandoffCeremonyProducer(
            resolvedDeferralQueue,
            resolvedBoundaryBiasProvider,
            new ForwardingLogger<HandoffCeremonyProducer>(resolvedLogger),
            scheduleResolver: resolvedScheduleResolver,
            personaStore: resolvedPersonaStore,
            speakerSnapshots: speakerSnapshotSource);

        // PLAN T534 (SPEC F191): the render-phase seam, extracted off Orchestrator — built from the
        // SAME resolvedTts/resolvedTime the Orchestrator itself used to take directly, plus whichever
        // announcementRenderer/announcementCopyWriter a spec passed through WithAnnouncementRenderer/
        // WithAnnouncementCopyWriter.
        var resolvedBreakRenderer = new BreakRenderer(resolvedTts, resolvedTime, announcementRenderer, announcementCopyWriter);

        // PLAN T536 (SPEC F192): the delivery-phase seam, extracted off Orchestrator — takes the
        // SAME resolvedEvents sink OrchestratorChain hands a spec below (PLAN T537 deleted
        // Orchestrator's own now-dead events parameter; BreakDelivery is the only reader left), and
        // the SAME estimator instance the Orchestrator's own patterEstimator: argument below passes,
        // so a spec that reads ObserveRendered calls through one sees them from the other too (see
        // this class's own remarks for why patterEstimator itself never defaults to a fake).
        var resolvedPatterEstimator = patterEstimator ?? new RollingPatterDurationEstimator();
        var resolvedBreakDelivery = new BreakDelivery(
            new ForwardingLogger<BreakDelivery>(resolvedLogger), resolvedEvents, resolvedPatterEstimator);

        var orchestrator = new Orchestrator(
            resolvedIdentityProvider,
            resolvedScopeProvider,
            resolvedCadenceProvider,
            resolvedRotationProvider,
            resolvedMusicSelectionPolicy,
            resolvedPersonaAccessor,
            resolvedLogger,
            resolvedDeferralQueue,
            resolvedTime,
            resolvedBoundaryBiasProvider,
            resolvedPlanner,
            resolvedHandoffCeremonyProducer,
            resolvedBreakRenderer,
            resolvedBreakDelivery,
            scheduleResolver: resolvedScheduleResolver,
            patterEstimator: resolvedPatterEstimator,
            imagingSettings: imagingSettings,
            crosstalkPlanner: crosstalkPlanner,
            observer: planObserver);

        return new OrchestratorChain(
            orchestrator,
            resolvedDeferralQueue,
            resolvedTime,
            resolvedTts,
            resolvedEvents,
            resolvedLogger,
            resolvedCatalog,
            patterEstimator);
    }
}
