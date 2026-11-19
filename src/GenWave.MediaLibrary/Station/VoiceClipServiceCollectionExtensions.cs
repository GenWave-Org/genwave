using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="IVoiceClipStore"/> (SPEC F219.12, PLAN T756): the same station-role connection string and lazy
/// data source as <see cref="VoicePackServiceCollectionExtensions.AddVoicePackStore"/>.
/// </summary>
public static class VoiceClipServiceCollectionExtensions
{
    public static IServiceCollection AddVoiceClipStore(this IServiceCollection services, string connectionString) =>
        services.AddSingleton<IVoiceClipStore>(_ => new VoiceClipRepository(
            new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
}
