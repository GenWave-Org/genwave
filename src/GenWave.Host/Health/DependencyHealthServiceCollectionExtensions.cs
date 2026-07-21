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

        return services;
    }
}
