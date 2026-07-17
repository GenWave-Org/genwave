namespace GenWave.Host.Seeding;

/// <summary>
/// Boot seed: branded safe-loop backstop (F27.6, STORY-080). One-shot idempotent
/// BackgroundService, gated on a marker key in station.settings that is deliberately excluded
/// from StationSettingsAllowlist (F27.10). Depends on ISafeSegmentAuthor
/// (AddGenWaveSafeSegmentAuthoring) and IStationSettingsStore/ILibraryRepository/IAdminLibraryWrite
/// (AddGenWaveStationSettings + AddMediaLibrary).
/// </summary>
static class SafeLoopSeedServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveSafeLoopSeed(this IServiceCollection services, IConfiguration configuration)
    {
        var stationConnStr = configuration.GetConnectionString("Station") ?? string.Empty;
        services.AddSingleton<ISafeLoopSeedMarkerStore>(_ => new SafeLoopSeedMarkerStore(stationConnStr));
        services.AddSingleton<SafeLoopSeeder>();
        services.AddHostedService<SafeLoopSeedHostedService>();
        return services;
    }
}
