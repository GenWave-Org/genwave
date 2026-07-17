using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

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
            .AddSingleton<IRenderBudgetProvider, OptionsMonitorRenderBudgetProvider>();

        return services;
    }
}
