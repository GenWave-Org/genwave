using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Orchestration;

namespace GenWave.Host.Options;

/// <summary>
/// Station/engine option binding + the live "read fresh per call" provider seams every consumer
/// shares. A module that wants a different scope/cadence/rotation policy overrides the matching
/// provider binding after this runs.
/// </summary>
static class StationOptionsServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveStationOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // Station config — validated at startup so a misconfigured station fails to boot rather
        // than silently broadcasting nothing (T009). Identity (Id/Name/Voice) is read live through
        // IStationIdentityProvider (SPEC F44.1, gitea-#196) — never a boot-frozen singleton.
        // StationOptionsValidator (IValidateOptions<StationOptions>) also runs at startup via
        // ValidateOnStart and guards Station:SafeScope:LibraryIds (non-empty, all-positive ids).
        services.AddSingleton<IValidateOptions<StationOptions>, StationOptionsValidator>();
        services
            .AddOptions<StationOptions>()
            .Bind(configuration.GetSection(StationOptions.Section))
            .ValidateDataAnnotations()
            .Validate(
                opts => opts.Scope.LibraryIds.Count > 0,
                "Station:Scope:LibraryIds must be non-empty (empty scope = silent station)")
            .ValidateOnStart();

        // OptionsMonitorStationClockProvider (gh-#117, below) needs a TimeProvider — TryAdd so a
        // host or test that already registers its own wins (the same TryAdd
        // AddGenWaveTts/AddGenWaveOrchestration each carry), and this method composes standalone.
        services.TryAddSingleton(TimeProvider.System);

        services
            .Configure<LiquidsoapOptions>(configuration.GetSection(LiquidsoapOptions.Section))
            .Configure<LoudnessOptions>(configuration.GetSection(LoudnessOptions.Section))
            // Live identity seam (SPEC F44.1, gitea-#196): Station:Name and Station:Voice are advertised
            // Live in the settings allowlist, so identity is read fresh through
            // IOptionsMonitor<StationOptions> on every call — never a boot-frozen singleton (the
            // retired StationContext). Consumed by the Orchestrator (SegmentRequest stamping),
            // AuthController (GET /api/stations), and the playout push path.
            .AddSingleton<IStationIdentityProvider, OptionsMonitorStationIdentityProvider>()
            // Live main-scope seam (SPEC F30.1): the ONE binding every scope-reading consumer
            // shares — Orchestrator, MediaController, ReenrichController, /media/* minimal API,
            // RandomSelectionProvider. Wraps IOptionsMonitor<StationOptions> and re-reads
            // CurrentValue on every call, so a live PUT /api/settings scope edit applies without
            // an api restart.
            .AddSingleton<IStationScopeProvider, OptionsMonitorStationScopeProvider>()
            // Live safe-scope seam (gh-#99): the mirror binding for Station:SafeScope:LibraryIds —
            // read by every safe-content exclusion check (rating endpoints, taste thumbs) so a live
            // SafeScope edit governs the very next check, same as the main-scope seam above.
            .AddSingleton<ISafeScopeProvider, OptionsMonitorSafeScopeProvider>()
            // Live cadence seam (gitea-#211 — F30.1's precedent applied to cadence): Station:Cadence:*
            // is advertised Live in the settings allowlist but used to be read from the
            // boot-frozen StationContext singleton (since retired). Wraps
            // IOptionsMonitor<StationOptions> and re-reads CurrentValue on every call, so a live
            // PUT /api/settings cadence edit applies without an api restart.
            .AddSingleton<ICadenceProvider, OptionsMonitorCadenceProvider>()
            // Live rotation seam (SPEC F41.6, same F30.1/gitea-#211 precedent): Station:Rotation:* is
            // advertised Live in the settings allowlist. Consumed by the Orchestrator (artist
            // separation) and PlayoutFeeder (anti-repeat window) — the SAME instance, so a live
            // PUT /api/settings rotation edit applies to both without an api restart.
            .AddSingleton<IRotationSettingsProvider, OptionsMonitorRotationSettingsProvider>()
            // Live render-budget seam (SPEC F44.2, closes gitea-#197 — the same F30.1/gitea-#211 precedent):
            // wraps IOptionsMonitor<TtsOptions> and re-reads CurrentValue on every call, so a live
            // PUT /api/settings edit to Tts:RenderBudgetSeconds applies to the very next unit's
            // renders.
            .AddSingleton<IRenderBudgetProvider, OptionsMonitorRenderBudgetProvider>()
            // Boundary-bias seam (SPEC F74.3, STORY-198): wraps IOptionsMonitor<StationOptions> so a
            // config-provider reload of Station:BoundaryBias:LookaheadMinutes reaches the
            // Orchestrator without a restart — but, unlike the four bindings above, this knob is
            // NOT joined to the settings allowlist (v1: boot/env-tunable only, no PUT write path).
            .AddSingleton<IBoundaryBiasProvider, OptionsMonitorBoundaryBiasProvider>()
            // Station-default envelope seam (SPEC F81.3, F91.4; STORY-212, STORY-241, PLAN T120):
            // Station:Envelope:* is advertised Live in the settings allowlist. Wraps
            // IOptionsMonitor<StationOptions> and re-reads CurrentValue on every call — the fallback
            // ScheduleResolver/ScheduleEnvelopeProvider (registered by AddGenWaveStationSettings)
            // both consult for a grid gap, a segment's NULL envelope field, or the process boot
            // window — so a live PUT /api/settings genre/energy edit still applies with no api
            // restart, exactly as it did before the format clock existed.
            .AddSingleton<IStationDefaultEnvelopeSource, OptionsMonitorStationDefaultEnvelopeSource>()
            // Live envelope seam (SPEC F91.7, STORY-241, PLAN T120): re-backs IEnvelopeProvider over
            // the schedule resolver (CachingScheduleResolver, registered by AddGenWaveStationSettings)
            // instead of the single 24/7 station-default value above — the Orchestrator's envelope-
            // aware pick and its per-pick debug line both observe the on-air segment's own envelope/
            // EnvelopeId with zero call-site change (F91.5).
            .AddSingleton<IEnvelopeProvider, ScheduleEnvelopeProvider>()
            // Live request-override seam (SPEC F87.6, STORY-227, PLAN T90): Station:Requests:OverrideEnvelope
            // is advertised Live in the settings allowlist. Wraps IOptionsMonitor<StationOptions> and
            // re-reads CurrentValue on every call, so a live PUT /api/settings edit applies to the
            // fulfillment rung's very next attempt with no api restart.
            .AddSingleton<IRequestOverrideEnvelopeProvider, OptionsMonitorRequestOverrideEnvelopeProvider>()
            // Live audience-posture seam (SPEC F95.1/F95.4, STORY-250, PLAN T111/T114): Station:Audience
            // is advertised Live in the settings allowlist. Wraps IOptionsMonitor<StationOptions> and
            // re-reads CurrentValue on every call (through the AudiencePostureParser fail-closed seam),
            // so a live PUT /api/settings edit reaches the very next candidate-pool query with no api
            // restart — the rotation/envelope queries and the request-catalog probe all resolve this
            // SAME binding (MediaLibraryServiceCollectionExtensions never registers a default).
            .AddSingleton<IAudiencePostureProvider, OptionsMonitorAudiencePostureProvider>()
            // Live station-clock seam (gh-#117): Station:Timezone is advertised Live in the
            // settings allowlist. Wraps IOptionsMonitor<StationOptions> and re-resolves the
            // timezone on every call, so a live PUT /api/settings edit reaches the very next LLM
            // prompt / SegmentRequest.LocalNow stamp with no api restart. Consumed by the
            // Orchestrator (LocalNow stamping), LlmCopyWriter (the prompt's clock line), and
            // PersonaController (preview requests) — the SAME instance, so the DJ's clock never
            // disagrees with itself. TimeProvider resolves to the TimeProvider.System TryAdd in
            // AddGenWaveTts/AddGenWaveOrchestration (or a test's own registration).
            .AddSingleton<IStationClockProvider, OptionsMonitorStationClockProvider>()
            // Show-flavor patter line (SPEC F116.3, STORY-308, PLAN T249): Station:Shows:PatterCadenceMinutes
            // is advertised Live in the settings allowlist. Wraps IOptionsMonitor<StationOptions> and
            // re-reads CurrentValue on every call, so a live PUT /api/settings edit reaches the very
            // next eligible break with no api restart — mirrors ICadenceProvider's own live-read shape.
            .AddSingleton<IShowPatterCadenceProvider, OptionsMonitorShowPatterCadenceProvider>()
            // The gate itself (mirrors IEnvelopeProvider/ScheduleEnvelopeProvider two lines above):
            // depends on CachingScheduleResolver, registered by AddGenWaveStationSettings, which runs
            // BEFORE this method in Program.cs. Plain AddSingleton, never TryAdd (the IContextPatterFactSource
            // ruling, ContextHostServiceCollectionExtensions' own remarks).
            //
            // The ACTUAL order in Program.cs: AddGenWaveStationOptions (this method) runs BEFORE
            // AddGenWaveTts, so GenWave.Tts's own TryAddSingleton<IShowFlavorLineSource,
            // NoOpShowFlavorLineSource> default finds a registration already present here and simply
            // never adds — one registration total. Had the order been reversed (Tts's TryAdd running
            // first, succeeding, then this AddSingleton running second), the outcome would still
            // resolve to ShowFlavorLineGate, but via a DIFFERENT mechanism: two registrations for the
            // same service type, with the LAST one added winning a single (non-enumerable)
            // GetRequiredService resolution — not a TryAdd no-op. The outcome is order-independent;
            // the mechanism is not. Verified end to end (not just by construction) by the SeamIndex
            // generator — a real WebApplicationFactory<Program> build — which lists
            // GenWave.Orchestration.ShowFlavorLineGate, not the NoOp, as this port's adapter in the
            // committed SEAMS.md.
            .AddSingleton<IShowFlavorLineSource, ShowFlavorLineGate>();

        return services;
    }
}
