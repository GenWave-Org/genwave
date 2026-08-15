using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IAvatarPackStore"/> (SPEC F128, STORY-332, PLAN T290). Deliberately
/// separate from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>:
/// <c>station.avatar_pack</c>(+<c>_item</c>) lives in the <c>station</c> schema/role
/// (<c>station_svc</c>), not <c>library</c> — the same "own connection string, own
/// <see cref="Lazy{T}"/> data source" shape <see cref="FontPackServiceCollectionExtensions"/>'s own
/// registration uses.
///
/// T290 ships this registration deliberately without a Host call site consuming
/// <see cref="IAvatarPackStore"/> anywhere (mirrors <see cref="FontPackServiceCollectionExtensions.AddFontPackStore"/>'s
/// own original shape): <c>POST /api/avatar-packs/{slug}/install</c> (T293) is the first write
/// consumer.
/// </summary>
public static class AvatarPackServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IAvatarPackStore"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>. The data source
    /// build is wrapped in a <see cref="Lazy{T}"/> — mirrors
    /// <see cref="FontPackServiceCollectionExtensions.AddFontPackStore"/>'s own remarks: merely
    /// resolving <see cref="IAvatarPackStore"/> must never be enough to trigger a connection attempt
    /// against an empty/dev-mode connection string.
    /// </summary>
    public static IServiceCollection AddAvatarPackStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IAvatarPackStore>(
            _ => new AvatarPackRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
