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
            new NpgsqlDataSourceBuilder(stationConnStr).Build(),
            sp.GetRequiredService<IActivePersonaAccessor>(),
            sp.GetRequiredService<ILogger<PersonaCardMigrator>>()));
        services.AddHostedService<PersonaCardMigrationHostedService>();

        return services;
    }
}
