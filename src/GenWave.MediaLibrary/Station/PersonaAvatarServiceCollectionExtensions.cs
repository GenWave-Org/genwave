using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IPersonaAvatarStore"/> (SPEC F128-F129, STORY-333, PLAN T290). Deliberately
/// separate from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>:
/// <c>station.persona_avatar</c> lives in the <c>station</c> schema/role (<c>station_svc</c>), not
/// <c>library</c> — the same "own connection string, own <see cref="Lazy{T}"/> data source" shape
/// <see cref="ThemeServiceCollectionExtensions"/>'s own registration uses.
///
/// <c>PersonaAvatarController</c> (T295) is the first write consumer of <see cref="IPersonaAvatarStore"/>
/// registered here.
/// </summary>
public static class PersonaAvatarServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IPersonaAvatarStore"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>. The data source
    /// build is wrapped in a <see cref="Lazy{T}"/> — mirrors
    /// <see cref="ThemeServiceCollectionExtensions.AddThemeStore"/>'s own remarks: merely resolving
    /// <see cref="IPersonaAvatarStore"/> must never be enough to trigger a connection attempt against
    /// an empty/dev-mode connection string.
    /// </summary>
    public static IServiceCollection AddPersonaAvatarStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IPersonaAvatarStore>(
            _ => new PersonaAvatarRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
