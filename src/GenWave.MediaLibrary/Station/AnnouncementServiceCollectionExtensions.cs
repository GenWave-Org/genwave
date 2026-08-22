using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="AnnouncementRepository"/> (SPEC F143, STORY-357, PLAN T337).
/// Deliberately separate from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>:
/// <c>station.announcement</c> lives in the <c>station</c> schema/role (<c>station_svc</c>), not
/// <c>library</c> — the same "own connection string, own <see cref="Lazy{T}"/> data source" shape
/// every other station-schema store in this directory uses (<see cref="RequestServiceCollectionExtensions"/>,
/// <see cref="ShowServiceCollectionExtensions"/>, ...).
///
/// Registered under its own concrete type rather than a public interface — unlike every sibling
/// registration in this directory, which keys on a <c>GenWave.Core.Abstractions</c> seam
/// (<see cref="ShowServiceCollectionExtensions.AddShowStore"/>'s own <c>IShowStore</c> key, etc.).
/// T337 ships no such seam for announcements: <c>IAnnouncementSource</c> (PLAN T338) is a narrower,
/// vend-only Core seam a Host-side adapter implements OVER this repository (PLAN T341), not this
/// repository itself, and T337 has no ordering dependency on T338 to build one prematurely. This
/// registration therefore ships dark exactly like <see cref="ShowServiceCollectionExtensions.AddShowStore"/>'s
/// own original T239 shape (no Host call site consumes it yet) — a future task widens
/// <see cref="AnnouncementRepository"/>'s accessibility, or adds an interface, the moment a
/// cross-assembly consumer actually needs one.
/// </summary>
public static class AnnouncementServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AnnouncementRepository"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>. The data source
    /// build is wrapped in a <see cref="Lazy{T}"/> — mirrors every other station-schema store's own
    /// remarks: merely resolving the store must never be enough to trigger a connection attempt
    /// against an empty/dev-mode connection string.
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
        return services.AddSingleton(
            _ => new AnnouncementRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
    }
}
