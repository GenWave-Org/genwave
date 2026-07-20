using System.Threading.Channels;
using GenWave.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for the booth log (SPEC F72.1-F72.3, STORY-195): the Postgres-backed store
/// (<see cref="IBoothLogAppender"/>/<see cref="IBoothLogReader"/>, over the SAME
/// <c>ConnectionStrings:Station</c> value <c>PersonaServiceCollectionExtensions</c> uses), the
/// bounded queue between <see cref="BoothLogWriter"/> (the <see cref="IStationEventSink"/> consumer,
/// a hot-path producer) and <see cref="BoothLogDrainService"/> (the background persister), and the
/// drain hosted service itself.
///
/// Deliberately does NOT bind <see cref="IStationEventSink"/> here — only <see cref="IBoothLogEventConsumer"/>,
/// a distinct seam. The Host composes <see cref="BoothLogWriter"/> alongside its other sink
/// consumer(s) (see <c>GenWave.Host.Playout.CompositeStationEventSink</c>) into the ONE binding that
/// wins container-wide — this project stays agnostic of who else is listening.
/// </summary>
public static class BoothLogServiceCollectionExtensions
{
    public static IServiceCollection AddBoothLog(
        this IServiceCollection services, string connectionString, IConfiguration configuration)
    {
        services
            .AddOptions<BoothLogOptions>()
            .Bind(configuration.GetSection(BoothLogOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Lazy data source (mirrors AddPersonaMemoryStore): resolving IBoothLogAppender/IBoothLogReader
        // must never be enough to trigger a connection attempt against an empty/dev-mode
        // ConnectionStrings:Station — only an actual AppendAsync/ReadAsync call does.
        services.AddSingleton(sp => new BoothLogRepository(
            new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build()),
            sp.GetRequiredService<IOptions<BoothLogOptions>>()));
        services.AddSingleton<IBoothLogAppender>(sp => sp.GetRequiredService<BoothLogRepository>());
        services.AddSingleton<IBoothLogReader>(sp => sp.GetRequiredService<BoothLogRepository>());

        // Bounded so a DB outage/backlog can never grow memory unbounded — hobby-scale event rate
        // (at most one row per track start/patter/mode change, never a hot inner loop) means 512 is
        // generous headroom. BoothLogWriter's Publish call uses TryWrite (never WriteAsync), so a
        // full queue drops the newest entry with a WARN instead of ever blocking the feeder tick or
        // a TTS render.
        services.AddSingleton(Channel.CreateBounded<BoothLogEntryRequest>(
            new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.Wait }));
        services.AddSingleton(sp => sp.GetRequiredService<Channel<BoothLogEntryRequest>>().Reader);
        services.AddSingleton(sp => sp.GetRequiredService<Channel<BoothLogEntryRequest>>().Writer);

        services.AddSingleton<BoothLogWriter>();
        services.AddSingleton<IBoothLogEventConsumer>(sp => sp.GetRequiredService<BoothLogWriter>());
        services.AddHostedService<BoothLogDrainService>();

        return services;
    }
}
