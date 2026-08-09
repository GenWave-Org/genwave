namespace GenWave.Context;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GenWave.Context.History;
using GenWave.Context.Weather;
using GenWave.Core.Abstractions;

/// <summary>
/// Composition of the context-provider seam's own registrations (SPEC F107.1/F108.1/F109.1) — mirrors
/// every other module's <c>*ServiceCollectionExtensions</c> shape. Deliberately narrow through T228:
/// registers each provider's own typed HTTP client plus the <see cref="IStationLocationProvider"/>/
/// <see cref="IContextCacheRootProvider"/> defaults, NOT <see cref="ContextPipeline"/> itself or a real
/// <see cref="IContextSettingsProvider"/> binding — those are PLAN T226's job (the Host ticker
/// composition root), which calls this method as one step of its own wiring.
/// </summary>
public static class ContextServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveContext(this IServiceCollection services)
    {
        // The clock ContextPipeline/WeatherContextProvider/HistoryContextProvider read for
        // FreshUntil/"now" — TryAdd so a host or test that already registers its own TimeProvider wins
        // (the same GenWave.Tts/GenWave.Orchestration/GenWave.MediaLibrary precedent).
        services.TryAddSingleton(TimeProvider.System);

        // Default binding (F108.1): a blank location is the correct fail-closed answer, not merely a
        // placeholder — see NoOpStationLocationProvider's own remarks. TryAdd so the T226 Host
        // IOptionsMonitor-backed implementation wins once it lands.
        services.TryAddSingleton<IStationLocationProvider>(NoOpStationLocationProvider.Instance);

        // Default binding (F109.2): a blank cache root is the correct fail-closed answer for
        // HistoryContextProvider, not merely a placeholder — see NoOpContextCacheRootProvider's own
        // remarks. TryAdd so the T226 Host binding (wrapping whichever options section owns the
        // writable cache volume) wins once it lands.
        services.TryAddSingleton<IContextCacheRootProvider>(NoOpContextCacheRootProvider.Instance);

        // Fixed, keyless Open-Meteo host (SPEC F108.1, T221 review's SSRF-safe framing) — never a
        // caller- or config-supplied URL. MaxResponseContentBufferSize bounds a forecast reply the
        // same way MusicBrainzYearLookup/OllamaMoodTagger bound theirs.
        services.AddHttpClient<WeatherContextProvider>(client =>
        {
            client.BaseAddress = new Uri(WeatherContextProvider.OpenMeteoBaseAddress);
            client.MaxResponseContentBufferSize = WeatherContextProvider.MaxResponseContentBytes;
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Fixed, keyless Wikimedia host (SPEC F109.1, same SSRF-safe framing) — never a caller- or
        // config-supplied URL.
        services.AddHttpClient<HistoryContextProvider>(client =>
        {
            client.BaseAddress = new Uri(HistoryContextProvider.WikimediaBaseAddress);
            client.MaxResponseContentBufferSize = HistoryContextProvider.MaxResponseContentBytes;
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Exposed under IContextProvider (SPEC F107.1) so ContextPipeline's IEnumerable<IContextProvider>
        // constructor parameter picks up both — AddSingleton (not TryAdd): each provider registers its
        // own entry alongside the others, and the pipeline needs all of them.
        services.AddSingleton<IContextProvider>(sp => sp.GetRequiredService<WeatherContextProvider>());
        services.AddSingleton<IContextProvider>(sp => sp.GetRequiredService<HistoryContextProvider>());

        return services;
    }
}
