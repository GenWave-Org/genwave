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
/// Builds a real <see cref="BreakPlanner"/> one seam at a time (SPEC F188, STORY-455, PLAN T521) —
/// mirrors <see cref="OrchestratorBuilder"/>'s own shape exactly: every required seam defaults to a
/// working fake/real collaborator, every seam has a <c>With*</c> override, and <see cref="Build"/>
/// resolves seams whose default depends on another seam's FINAL value (the schedule resolver and
/// persona store feed the persona accessor) in dependency order.
///
/// <para>
/// <see cref="BreakPlannerChain"/> types every slot to the seam type its <c>With*</c> accepts, so
/// <see cref="Build"/> never throws for an override that compiles. A spec that needs a fake's own
/// members on a default-feature-dark seam (announcement source, ad vend/cadence, crosstalk planner,
/// station clock, context settings, imaging settings, voice lister) passes its own fake through the
/// matching <c>With*</c> and keeps the reference itself — this builder never defaults those to a
/// fake, only to <see langword="null"/>, exactly as <see cref="OrchestratorBuilder"/> does for the
/// same seams today.
/// </para>
/// </summary>
public sealed class BreakPlannerBuilder
{
    IActivePersonaAccessor? personaAccessor;
    ILogger<BreakPlanner>? logger;
    IRenderBudgetProvider? renderBudgetProvider;
    SpeechDeferralQueue? deferralQueue;
    TimeProvider? timeProvider;
    IStationScopeProvider? scopeProvider;
    IStationClockProvider? stationClock;
    CachingScheduleResolver? scheduleResolver;
    IContextSettingsProvider? contextSettings;
    IMediaCatalog? catalog;
    IStationImagingSettingsProvider? imagingSettings;
    CrosstalkPlanner? crosstalkPlanner;
    IAnnouncementSource? announcementSource;
    ITtsVoiceLister? voiceLister;
    IAdCadenceProvider? adCadenceProvider;
    IAdSpotVend? adSpotVend;
    IPersonaStore? personaStore;
    ISpeakerSnapshotSource? speakerSnapshotSource;

    // Only meaningful when scheduleResolver is unset — feeds the default schedule-resolver chain's
    // FakeScheduleStore (see WithSchedule).
    ScheduleWeekSnapshot snapshot = new([]);
    bool scheduleSet;

    /// <summary>Every trivial "set the seam, return this" setter below funnels through here — see
    /// <see cref="OrchestratorBuilder"/>'s own identical helper.</summary>
    BreakPlannerBuilder With<T>(ref T? slot, T? value) where T : class
    {
        slot = value;
        return this;
    }

    /// <summary>Overrides the active-persona accessor seam. Defaults to a real accessor over the final schedule resolver and persona store.</summary>
    public BreakPlannerBuilder WithPersonaAccessor(IActivePersonaAccessor accessor) => With(ref personaAccessor, accessor);

    /// <summary>Overrides the logger seam. Defaults to a <see cref="CapturingLogger{T}"/>.</summary>
    public BreakPlannerBuilder WithLogger(ILogger<BreakPlanner> plannerLogger) => With(ref logger, plannerLogger);

    /// <summary>Overrides the render budget seam. Defaults to a fixed 5-second <c>FakeRenderBudgetProvider</c>.</summary>
    public BreakPlannerBuilder WithRenderBudget(IRenderBudgetProvider renderBudget) => With(ref renderBudgetProvider, renderBudget);

    /// <summary>Convenience — wraps <paramref name="budget"/> in a <see cref="FakeRenderBudgetProvider"/>.</summary>
    public BreakPlannerBuilder WithRenderBudget(TimeSpan budget) => WithRenderBudget(new FakeRenderBudgetProvider(budget));

    /// <summary>Overrides the speech deferral queue seam. Defaults to a queue over the final clock.</summary>
    public BreakPlannerBuilder WithDeferralQueue(SpeechDeferralQueue queue) => With(ref deferralQueue, queue);

    /// <summary>Overrides the clock seam. Defaults to a <see cref="FakeTimeProvider"/> at a fixed instant.</summary>
    public BreakPlannerBuilder WithTime(TimeProvider time) => With(ref timeProvider, time);

    /// <summary>Convenience — a <see cref="FakeTimeProvider"/> at <paramref name="now"/>.</summary>
    public BreakPlannerBuilder WithNow(DateTimeOffset now) => WithTime(new FakeTimeProvider(now));

    /// <summary>Overrides the library scope seam. Defaults to a fixed <c>FakeStationScopeProvider</c>.</summary>
    public BreakPlannerBuilder WithScope(IStationScopeProvider scope) => With(ref scopeProvider, scope);

    /// <summary>Overrides the station clock seam. Defaults to <see langword="null"/> (feature-dark — BreakPlanner falls back to <see cref="TimeProvider"/>'s own UTC now).</summary>
    public BreakPlannerBuilder WithStationClock(IStationClockProvider? clock) => With(ref stationClock, clock);

    /// <summary>Overrides the schedule resolver seam outright. Exclusive with <see cref="WithSchedule"/> — <see cref="Build"/> throws if both are set.</summary>
    public BreakPlannerBuilder WithScheduleResolver(CachingScheduleResolver resolver) => With(ref scheduleResolver, resolver);

    /// <summary>Convenience — keeps the default schedule-resolver chain but seeds its <see cref="FakeScheduleStore"/> with <paramref name="week"/>. Exclusive with <see cref="WithScheduleResolver"/> — <see cref="Build"/> throws if both are set.</summary>
    public BreakPlannerBuilder WithSchedule(ScheduleWeekSnapshot week)
    {
        snapshot = week;
        scheduleSet = true;
        return this;
    }

    /// <summary>Overrides the per-key context-persona seam. Defaults to <see langword="null"/> (feature-dark).</summary>
    public BreakPlannerBuilder WithContextSettings(IContextSettingsProvider? settings) => With(ref contextSettings, settings);

    /// <summary>Overrides the media catalog seam. Defaults to a single-track <see cref="FakeMediaCatalog"/>.</summary>
    public BreakPlannerBuilder WithCatalog(IMediaCatalog mediaCatalog) => With(ref catalog, mediaCatalog);

    /// <summary>Overrides the station imaging settings seam. Defaults to <see langword="null"/> (feature-dark).</summary>
    public BreakPlannerBuilder WithImagingSettings(IStationImagingSettingsProvider? settings) => With(ref imagingSettings, settings);

    /// <summary>Overrides the crosstalk planner seam. Defaults to <see langword="null"/> (feature-dark).</summary>
    public BreakPlannerBuilder WithCrosstalkPlanner(CrosstalkPlanner? planner) => With(ref crosstalkPlanner, planner);

    /// <summary>Overrides the announcement source seam. Defaults to <see langword="null"/> (feature-dark).</summary>
    public BreakPlannerBuilder WithAnnouncementSource(IAnnouncementSource? source) => With(ref announcementSource, source);

    /// <summary>Overrides the TTS voice lister seam. Defaults to <see langword="null"/> (feature-dark).</summary>
    public BreakPlannerBuilder WithVoiceLister(ITtsVoiceLister? lister) => With(ref voiceLister, lister);

    /// <summary>Overrides the ad cadence seam. Defaults to <see langword="null"/> (feature-dark — no ad ever fires).</summary>
    public BreakPlannerBuilder WithAdCadence(IAdCadenceProvider? adCadence) => With(ref adCadenceProvider, adCadence);

    /// <summary>Overrides the ad spot vend seam. Defaults to <see langword="null"/> (feature-dark — no ad ever fires).</summary>
    public BreakPlannerBuilder WithAdSpotVend(IAdSpotVend? vend) => With(ref adSpotVend, vend);

    /// <summary>Overrides the persona store seam. Defaults to an empty <see cref="FakePersonaStore"/>.</summary>
    public BreakPlannerBuilder WithPersonaStore(IPersonaStore store) => With(ref personaStore, store);

    /// <summary>Overrides the speaker snapshot source seam (PLAN T527, SPEC F188.4). Defaults to <see langword="null"/> (feature-dark — no slot's request is ever stamped, SPEC F189.6).</summary>
    public BreakPlannerBuilder WithSpeakerSnapshotSource(ISpeakerSnapshotSource? source) => With(ref speakerSnapshotSource, source);

    /// <summary>
    /// Resolves every unset seam to its default and constructs the <see cref="BreakPlanner"/>,
    /// returning it alongside every collaborator a spec has ever needed to assert on directly.
    /// </summary>
    public BreakPlannerChain Build()
    {
        if (scheduleResolver is not null && scheduleSet)
        {
            throw new InvalidOperationException("WithSchedule and WithScheduleResolver are exclusive");
        }

        var resolvedTime = timeProvider ?? new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var resolvedCatalog = catalog ?? new FakeMediaCatalog(TestData.MakeTrackRef("t1"));

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

        var resolvedLogger = logger ?? new CapturingLogger<BreakPlanner>();

        var resolvedDeferralQueue = deferralQueue ?? new SpeechDeferralQueue(resolvedTime);

        var resolvedScopeProvider = scopeProvider ?? new FakeStationScopeProvider(new LibraryScope([1L]));
        var resolvedRenderBudgetProvider = renderBudgetProvider ?? new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5));

        var planner = new BreakPlanner(
            resolvedPersonaAccessor,
            resolvedLogger,
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
            announcementSource: announcementSource,
            voiceLister: voiceLister,
            adCadenceProvider: adCadenceProvider,
            adSpotVend: adSpotVend,
            personaStore: resolvedPersonaStore,
            speakerSnapshots: speakerSnapshotSource);

        return new BreakPlannerChain(
            planner,
            resolvedDeferralQueue,
            resolvedTime,
            resolvedLogger,
            resolvedCatalog,
            resolvedScheduleResolver,
            resolvedPersonaStore);
    }
}
