using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IShowStore"/> (SPEC F115.1, STORY-305, PLAN T239). Deliberately separate
/// from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>: <c>station.show</c>
/// lives in the <c>station</c> schema/role (<c>station_svc</c>), not <c>library</c> — the same "own
/// connection string, own <see cref="Lazy{T}"/> data source" shape
/// <see cref="PersonaServiceCollectionExtensions"/>'s and <see cref="ThemeServiceCollectionExtensions"/>'s
/// registrations use.
///
/// T239 ships this registration deliberately without a Host call site (mirrors
/// <see cref="ThemeServiceCollectionExtensions.AddThemeStore"/>'s own original T181 shape — "no
/// consumer lands with this seam"): <c>/api/shows</c> (PLAN T240) is the first consumer.
/// </summary>
public static class ShowServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IShowStore"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>. The data source
    /// build is wrapped in a <see cref="Lazy{T}"/> — mirrors
    /// <see cref="PersonaServiceCollectionExtensions.AddPersonaStore"/>'s own remarks: merely resolving
    /// <see cref="IShowStore"/> must never be enough to trigger a connection attempt against an
    /// empty/dev-mode connection string.
    /// </summary>
    public static IServiceCollection AddShowStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IShowStore>(
            _ => new ShowRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
