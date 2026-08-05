using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IThemeStore"/> (SPEC F103.7, STORY-271, PLAN T181). Deliberately
/// separate from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>:
/// <c>station.theme</c> lives in the <c>station</c> schema/role (<c>station_svc</c>), not
/// <c>library</c> — the same "own connection string, own <see cref="Lazy{T}"/> data source" shape
/// <see cref="PersonaServiceCollectionExtensions"/>'s registrations use.
///
/// T181 ships this registration deliberately without a Host call site consuming
/// <see cref="IThemeStore"/> anywhere (mirrors <see cref="PersonaServiceCollectionExtensions.AddPersonaTasteStore"/>'s
/// own original shape): <c>ThemeCatalog</c> (T182) and the theme import route (T184) are the first
/// consumers, landing in later tasks.
/// </summary>
public static class ThemeServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IThemeStore"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>. The data
    /// source build is wrapped in a <see cref="Lazy{T}"/> — mirrors
    /// <see cref="PersonaServiceCollectionExtensions.AddPersonaStore"/>'s own remarks: merely
    /// resolving <see cref="IThemeStore"/> must never be enough to trigger a connection attempt
    /// against an empty/dev-mode connection string.
    /// </summary>
    public static IServiceCollection AddThemeStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IThemeStore>(
            _ => new ThemeRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
