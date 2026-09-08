using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IJinglePackStore"/> (SPEC F165.2/F165.5/F165.6, STORY-399/401,
/// PLAN T414). Unlike every other <c>Add*PackStore</c> extension in this namespace, this one takes
/// TWO connection strings — <see cref="JinglePackRepository"/>'s own two-schema-role reality (see
/// that type's own remarks) means it needs its own dedicated data source into BOTH
/// <c>station_svc</c> and <c>library_svc</c>, neither of which this call may assume is already
/// registered in the container under the shape it needs.
/// </summary>
public static class JinglePackServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IJinglePackStore"/> as a singleton over two dedicated
    /// <see cref="NpgsqlDataSource"/>s built from <paramref name="stationConnectionString"/> and
    /// <paramref name="libraryConnectionString"/>, each wrapped in its own <see cref="Lazy{T}"/> —
    /// merely resolving <see cref="IJinglePackStore"/> must never be enough to trigger a connection
    /// attempt against an empty/dev-mode connection string, the same discipline
    /// <see cref="VoicePackServiceCollectionExtensions.AddVoicePackStore"/> follows for its own single
    /// data source.
    /// </summary>
    public static IServiceCollection AddJinglePackStore(
        this IServiceCollection services, string stationConnectionString, string libraryConnectionString) =>
        services.AddSingleton<IJinglePackStore>(sp => new JinglePackRepository(
            new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(stationConnectionString).Build()),
            new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(libraryConnectionString).Build()),
            sp.GetRequiredService<ILogger<JinglePackRepository>>()));
}
