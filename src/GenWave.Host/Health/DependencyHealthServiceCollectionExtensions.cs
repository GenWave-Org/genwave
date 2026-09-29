using GenWave.Host.Engine;
using GenWave.Host.Options;
using GenWave.Tts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace GenWave.Host.Health;

/// <summary>
/// Wires the background dependency-health probe loop (SPEC F70.2, STORY-187): validated cadence
/// options + the hosted service. <see cref="DependencyHealthProber"/> and every
/// <see cref="IDependencyProbe"/> (Ollama, Kokoro) are already registered by
/// <c>AddGenWaveTts</c> — this extension only adds the options this Host-owned loop needs and the
/// <see cref="DependencyHealthProbeService"/> that drives them, staying entirely unaware of which
/// probes exist.
/// <para>
/// Also wires the engine-settings verdict check (SPEC F213.5, STORY-488, PLAN T602) on the SAME
/// validated cadence — it is registered here, not its own extension, because it deliberately shares
/// <see cref="DependencyHealthOptions"/> rather than introducing a second interval knob (see
/// <see cref="EngineSettingsCheckService"/>'s own remarks). <see cref="IEngineTuningReader"/> is
/// registered by <c>AddGenWavePlayout</c>, which Program.cs runs before this call.
/// </para>
/// </summary>
public static class DependencyHealthServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveDependencyHealth(
        this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<DependencyHealthOptions>()
            .Bind(configuration.GetSection(DependencyHealthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<DependencyHealthProber>();
        services.AddHostedService<DependencyHealthProbeService>();

        // The engine-settings verdict cache is a public singleton so StatusController (public) can
        // take it as a constructor parameter without widening EngineSettingsVerdict/EngineSettingsState
        // themselves past internal (see EngineSettingsStatus's own remarks) — registered here so it
        // exists (reading "unknown", AC11) even before the check's first cycle runs.
        services.AddSingleton<EngineSettingsStatus>();
        services.AddSingleton<EngineSettingsCheck>();
        services.AddHostedService<EngineSettingsCheckService>();

        return services;
    }
}
