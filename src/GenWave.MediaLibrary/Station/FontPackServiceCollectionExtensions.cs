using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IFontPackStore"/> (SPEC F104, STORY-282, PLAN T198). Deliberately
/// separate from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>:
/// <c>station.font_pack</c>(+<c>_face</c>) lives in the <c>station</c> schema/role
/// (<c>station_svc</c>), not <c>library</c> — the same "own connection string, own
/// <see cref="Lazy{T}"/> data source" shape <see cref="ThemeServiceCollectionExtensions"/>'s own
/// registration uses.
///
/// T198 ships this registration deliberately without any call site consuming
/// <see cref="IFontPackStore"/> anywhere (mirrors
/// <see cref="ThemeServiceCollectionExtensions.AddThemeStore"/>'s own original shape): the future
/// font-pack install route (PLAN T199) is the first write consumer, <c>InstalledFontCatalog</c>
/// (T199/T200) and the library page (T203) the first read consumers — none of them exist yet, so
/// nothing in <c>GenWave.Host</c> calls this extension method until that task wires it in.
/// </summary>
public static class FontPackServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFontPackStore"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>. The data
    /// source build is wrapped in a <see cref="Lazy{T}"/> — mirrors
    /// <see cref="ThemeServiceCollectionExtensions.AddThemeStore"/>'s own remarks: merely resolving
    /// <see cref="IFontPackStore"/> must never be enough to trigger a connection attempt against an
    /// empty/dev-mode connection string.
    /// </summary>
    public static IServiceCollection AddFontPackStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IFontPackStore>(
            _ => new FontPackRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
