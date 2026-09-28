using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="ISchemaJournal"/> (SPEC F211.4, STORY-484, PLAN T592). Deliberately
/// separate from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>:
/// <c>station.schema_migration</c> lives in the <c>station</c> schema/role (<c>station_svc</c>), not
/// <c>library</c> — the same "own connection string, own <see cref="Lazy{T}"/> data source" shape
/// <see cref="ThemeServiceCollectionExtensions"/>'s own registration uses.
///
/// T592 ships this registration deliberately without a Host call site consuming
/// <see cref="ISchemaJournal"/> anywhere (mirrors <see cref="ThemeServiceCollectionExtensions.AddThemeStore"/>'s
/// own original shape): PLAN T593's <c>/api/status</c> is the first consumer.
/// </summary>
public static class SchemaJournalServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISchemaJournal"/> as a singleton over a dedicated
    /// <see cref="NpgsqlDataSource"/> built from <paramref name="connectionString"/>. The data
    /// source build is wrapped in a <see cref="Lazy{T}"/> — mirrors
    /// <see cref="ThemeServiceCollectionExtensions.AddThemeStore"/>'s own remarks: merely
    /// resolving <see cref="ISchemaJournal"/> must never be enough to trigger a connection attempt
    /// against an empty/dev-mode connection string.
    /// </summary>
    public static IServiceCollection AddSchemaJournal(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<ISchemaJournal>(
            _ => new SchemaJournalRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
