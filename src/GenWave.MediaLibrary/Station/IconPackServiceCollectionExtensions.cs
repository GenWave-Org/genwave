using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IIconPackStore"/> (SPEC F130, STORY-337, PLAN T290). Deliberately separate
/// from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>: <c>station.icon_pack</c>
/// lives in the <c>station</c> schema/role (<c>station_svc</c>), not <c>library</c> — the same "own
/// connection string, own <see cref="Lazy{T}"/> data source" shape
/// <see cref="ThemeServiceCollectionExtensions"/>'s own registration uses.
///
/// T290 ships this registration deliberately without a Host call site consuming
/// <see cref="IIconPackStore"/> anywhere (mirrors <see cref="ThemeServiceCollectionExtensions.AddThemeStore"/>'s
/// own original shape): <c>POST /api/icon-packs/{slug}/install</c> (T303) is the first write consumer.
/// </summary>
public static class IconPackServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IIconPackStore"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>. The data source
    /// build is wrapped in a <see cref="Lazy{T}"/> — mirrors
    /// <see cref="ThemeServiceCollectionExtensions.AddThemeStore"/>'s own remarks: merely resolving
    /// <see cref="IIconPackStore"/> must never be enough to trigger a connection attempt against an
    /// empty/dev-mode connection string.
    /// </summary>
    public static IServiceCollection AddIconPackStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IIconPackStore>(
            _ => new IconPackRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
