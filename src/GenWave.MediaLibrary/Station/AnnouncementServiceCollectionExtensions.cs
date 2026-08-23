using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using GenWave.Core.Abstractions;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="AnnouncementRepository"/> (SPEC F143, STORY-357, PLAN T337).
/// Deliberately separate from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>:
/// <c>station.announcement</c> lives in the <c>station</c> schema/role (<c>station_svc</c>), not
/// <c>library</c> — the same "own connection string, own <see cref="Lazy{T}"/> data source" shape
/// every other station-schema store in this directory uses (<see cref="RequestServiceCollectionExtensions"/>,
/// <see cref="ShowServiceCollectionExtensions"/>, ...).
///
/// <b>Registered under <see cref="IAnnouncementStore"/> (PLAN T339)</b> — the same
/// <see cref="ShowServiceCollectionExtensions.AddShowStore"/>-shaped "key on the
/// <c>GenWave.Core.Abstractions</c> seam" registration every sibling in this directory uses. T337
/// shipped this call keyed on the bare concrete type (no seam existed yet); <c>IAnnouncementSource</c>
/// (PLAN T338/T341) is a DIFFERENT, narrower vend-only seam a Host-side adapter still implements OVER
/// this repository — that one stays a separate registration when T341 lands it. This one only ever
/// resolves <see cref="AnnouncementRepository"/> itself, so keying on its own interface is enough.
/// </summary>
public static class AnnouncementServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AnnouncementRepository"/> as a singleton, keyed on <see cref="IAnnouncementStore"/>,
    /// over a dedicated <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>.
    /// The data source build is wrapped in a <see cref="Lazy{T}"/> — mirrors every other station-schema
    /// store's own remarks: merely resolving the store must never be enough to trigger a connection
    /// attempt against an empty/dev-mode connection string.
    ///
    /// Also registers <see cref="AnnouncementStateTypeHandler"/> — this store's only consumer, so
    /// unlike <see cref="DateOnlyTypeHandler"/>'s shared <c>AddMediaLibrary</c> home, the registration
    /// lives here instead (see that handler's own remarks). <see cref="SqlMapper.AddTypeHandler{T}"/>
    /// writes into a static, process-wide dictionary keyed by type, so re-registering the same
    /// <see cref="AnnouncementStateTypeHandler.Instance"/> on a second call is a harmless overwrite.
    /// </summary>
    public static IServiceCollection AddAnnouncementStore(this IServiceCollection services, string connectionString)
    {
        SqlMapper.AddTypeHandler(AnnouncementStateTypeHandler.Instance);
        return services.AddSingleton<IAnnouncementStore>(
            _ => new AnnouncementRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
    }
}
