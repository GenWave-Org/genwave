using GenWave.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>DI wiring for the utterance store (SPEC F218, STORY-532).</summary>
public static class UtteranceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IUtteranceStore"/> (SPEC F218, STORY-532) the same lazy way
    /// <see cref="PersonaServiceCollectionExtensions.AddPersonaStore"/> registers <see cref="IPersonaStore"/>, and binds/validates
    /// <see cref="UtteranceOptions"/> (<c>Memory:UtteranceRetentionDays</c>) from
    /// <paramref name="configuration"/>.
    /// </summary>
    public static IServiceCollection AddUtteranceStore(
        this IServiceCollection services, string connectionString, IConfiguration configuration)
    {
        services
            .AddOptions<UtteranceOptions>()
            .Bind(configuration.GetSection(UtteranceOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services.AddSingleton<IUtteranceStore>(sp =>
            new UtteranceRepository(
                new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build()),
                sp.GetRequiredService<IOptionsMonitor<UtteranceOptions>>()));
    }
}
