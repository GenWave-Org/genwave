using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IPersonaStore"/> (SPEC F35.1, STORY-118/120) — first consumer is
/// <c>PersonaController</c> (Host), STORY-120. Deliberately separate from
/// <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>: personas live in the
/// <c>station</c> schema/role (<c>station_svc</c>, the same connection the F19 settings overlay
/// uses), not <c>library</c> — a distinct <see cref="NpgsqlDataSource"/> on its own connection
/// string rather than folded into the library's.
/// </summary>
public static class PersonaServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IPersonaStore"/> over a dedicated <see cref="NpgsqlDataSource"/> built
    /// from <paramref name="connectionString"/> (the same value the caller passes to
    /// <c>ConnectionStrings:Station</c>-backed services). The data source is constructed lazily
    /// inside the factory — not eagerly here — so an empty connection string (dev/test hosts that
    /// never configure Station) never blocks composition; the failure only surfaces if a request
    /// actually resolves <see cref="IPersonaStore"/>.
    /// </summary>
    public static IServiceCollection AddPersonaStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IPersonaStore>(
            _ => new PersonaRepository(new NpgsqlDataSourceBuilder(connectionString).Build()));
}
