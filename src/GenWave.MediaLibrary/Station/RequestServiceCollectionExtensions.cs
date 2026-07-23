using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IRequestStore"/> (SPEC F87, STORY-224, PLAN T86) — first consumer is
/// the T87 intake endpoint. Deliberately separate from
/// <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>: <c>station.request</c>
/// lives in the <c>station</c> schema/role (<c>station_svc</c>), not <c>library</c> — the same
/// "own connection string, own <see cref="Lazy{T}"/> data source" shape
/// <see cref="PersonaServiceCollectionExtensions"/>'s registrations use.
/// </summary>
public static class RequestServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRequestStore"/> over a dedicated <see cref="NpgsqlDataSource"/> built
    /// from <paramref name="connectionString"/> (the same value the caller passes to every other
    /// <c>ConnectionStrings:Station</c>-backed registration). The data source build is wrapped in a
    /// <see cref="Lazy{T}"/> — mirrors <see cref="PersonaServiceCollectionExtensions.AddPersonaStore"/>'s
    /// own remarks: merely resolving <see cref="IRequestStore"/> must never be enough to trigger a
    /// connection attempt against an empty/dev-mode connection string.
    ///
    /// <paramref name="wishRetentionHours"/> is <c>Requests:WishRetentionHours</c>'s already-resolved
    /// value (see <see cref="RequestRepository"/>'s own remarks for why this arrives as a plain
    /// value rather than a bound options type).
    /// </summary>
    public static IServiceCollection AddRequestStore(
        this IServiceCollection services, string connectionString, int wishRetentionHours) =>
        services.AddSingleton<IRequestStore>(
            _ => new RequestRepository(
                new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build()),
                wishRetentionHours));
}
