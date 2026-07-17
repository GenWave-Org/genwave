using GenWave.Core.Abstractions;
using GenWave.Host.Options;
using GenWave.MediaLibrary.Station;

namespace GenWave.Host.Configuration;

/// <summary>
/// Wires everything that rides <c>ConnectionStrings:Station</c> (the <c>station_svc</c> role):
/// the settings overlay, the settings store/validator, and the persona store + live accessor.
/// </summary>
static class StationSettingsHostingExtensions
{
    public static WebApplicationBuilder AddGenWaveStationSettings(this WebApplicationBuilder builder)
    {
        var stationConnStr = builder.Configuration.GetConnectionString("Station") ?? string.Empty;

        // ── Station settings overlay (STORY-042, Epic I) ────────────────────
        // The custom provider is registered AFTER env/appsettings so a row in station.settings wins
        // over file/env defaults. The source is created here (before builder.Build) so we can
        // register the singleton store against the same source instance — the store calls
        // source.BuiltProvider.Reload() after each write, which raises the change token for
        // IOptionsMonitor<T>.
        var stationSettingsSource = new StationSettingsConfigurationSource(stationConnStr);
        builder.Configuration.AddEnvironmentVariables();   // ensure env vars are loaded before we append
        builder.Configuration.Sources.Add(stationSettingsSource);

        // Station settings store — singleton; same source the provider was built from so writes
        // can signal the live-reload change token. Factory (not instance) registration so the
        // store picks up whatever IStationEventSink binding wins once ALL extensions have run —
        // this extension executes first, before the sink is bound (gitea-#246).
        builder.Services.AddSingleton<IStationSettingsStore>(sp =>
            new StationSettingsStore(stationConnStr, stationSettingsSource, sp.GetRequiredService<IStationEventSink>()));

        // Settings validator — stateless, singleton.  Used by SettingsController.
        builder.Services.AddSingleton<SettingValidator>();

        // ── Persona store (SPEC F35.1/F35.4, STORY-120) ─────────────────────
        // Same ConnectionStrings:Station value as the settings overlay above — a dedicated
        // NpgsqlDataSource for station.persona (station_svc role), built lazily inside
        // AddPersonaStore so an empty/dev-mode connection string never blocks boot (mirrors
        // AddMediaLibrary's own lazy data-source factory); the failure only surfaces if a request
        // actually resolves IPersonaStore.
        builder.Services.AddPersonaStore(stationConnStr);

        // ActivePersonaAccessor (SPEC F35.2, F35.5): the ONE seam the Orchestrator and the
        // preview/status endpoints read the live active persona through — re-reads
        // IOptionsMonitor<StationOptions> + IPersonaStore per call, never a stale snapshot, never
        // throws (WARN + null on any miss).
        builder.Services.AddSingleton<IActivePersonaAccessor, ActivePersonaAccessor>();

        return builder;
    }
}
