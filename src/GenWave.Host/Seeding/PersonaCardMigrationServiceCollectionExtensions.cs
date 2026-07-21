using GenWave.Core.Abstractions;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.Host.Seeding;

/// <summary>
/// Wires <see cref="PersonaCardMigrator"/> (SPEC F71.1/F71.2, STORY-192). Builds its own
/// <see cref="NpgsqlDataSource"/> from <c>ConnectionStrings:Station</c> inside the factory — mirrors
/// <see cref="Configuration.StationSettingsHostingExtensions.AddGenWaveStationSettings"/>'s own
/// <c>AddPersonaStore</c> call, rather than registering a second, ambiguous bare
/// <see cref="NpgsqlDataSource"/> singleton in the container — so an empty/dev-mode connection
/// string still never blocks composition; the failure only surfaces when the hosted service runs.
/// The build itself is wrapped in a <see cref="Lazy{T}"/> (bug fix — <see cref="PersonaCardMigrator"/>
/// only dereferences <c>.Value</c> from inside its own <c>RunAsync</c> try/catch): an unwrapped
/// <c>Build()</c> call right here throws the moment <c>IHost.StartAsync</c> resolves this hosted
/// service — DI singleton factories run eagerly during composition, before any hosted service's own
/// try/catch is even reached — killing host boot outright on the documented persona-less
/// (<c>ConnectionStrings:Station</c> = <c>""</c>) configuration. Mirrors <c>AddPersonaStore</c>,
/// <c>AddPersonaMemoryStore</c> (<see cref="MediaLibrary.Station.PersonaServiceCollectionExtensions"/>)
/// and <c>AddBoothLog</c> (<see cref="MediaLibrary.Station.BoothLogServiceCollectionExtensions"/>),
/// which wrap the same way for the same reason.
/// Depends on <see cref="IActivePersonaAccessor"/> (registered by
/// <see cref="Configuration.StationSettingsHostingExtensions.AddGenWaveStationSettings"/>, which
/// Program.cs calls before this extension).
/// </summary>
static class PersonaCardMigrationServiceCollectionExtensions
{
    public static IServiceCollection AddGenWavePersonaCardMigration(
        this IServiceCollection services, IConfiguration configuration)
    {
        var stationConnStr = configuration.GetConnectionString("Station") ?? string.Empty;

        services.AddSingleton(sp => new PersonaCardMigrator(
            new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(stationConnStr).Build()),
            sp.GetRequiredService<IActivePersonaAccessor>(),
            sp.GetRequiredService<ILogger<PersonaCardMigrator>>()));
        services.AddHostedService<PersonaCardMigrationHostedService>();

        return services;
    }
}
