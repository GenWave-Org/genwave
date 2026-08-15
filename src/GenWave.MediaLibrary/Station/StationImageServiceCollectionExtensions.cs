using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IStationImageStore"/> (SPEC F131, STORY-339, PLAN T290, gh-#15).
/// Deliberately separate from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>:
/// <c>station.station_image</c> lives in the <c>station</c> schema/role (<c>station_svc</c>), not
/// <c>library</c> — the same "own connection string, own <see cref="Lazy{T}"/> data source" shape
/// <see cref="ThemeServiceCollectionExtensions"/>'s own registration uses.
///
/// T290 ships this registration deliberately without a Host call site consuming
/// <see cref="IStationImageStore"/> anywhere (mirrors <see cref="ThemeServiceCollectionExtensions.AddThemeStore"/>'s
/// own original shape): <c>StationImageController</c>'s <c>PUT</c>/<c>DELETE</c> (T307) is the first
/// write consumer.
/// </summary>
public static class StationImageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IStationImageStore"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>. The data source
    /// build is wrapped in a <see cref="Lazy{T}"/> — mirrors
    /// <see cref="ThemeServiceCollectionExtensions.AddThemeStore"/>'s own remarks: merely resolving
    /// <see cref="IStationImageStore"/> must never be enough to trigger a connection attempt against
    /// an empty/dev-mode connection string.
    /// </summary>
    public static IServiceCollection AddStationImageStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IStationImageStore>(
            _ => new StationImageRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
