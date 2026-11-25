using System.Threading.Channels;
using GenWave.Core.TalkRules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>DI wiring for the talk check log (SPEC F220.29, STORY-566).</summary>
public static class TalkCheckServiceCollectionExtensions
{
    /// <summary>
    /// Bounded so a database outage can never grow memory: about a thousand rows a day against 256 slots is
    /// ample headroom. <see cref="BoundedChannelFullMode.Wait"/> with <c>TryWrite</c> only: a full queue refuses
    /// the new row instead of ever blocking the writer.
    /// </summary>
    const int QueueCapacity = 256;

    /// <summary>
    /// Registers <see cref="ITalkCheckLog"/> (queue + <see cref="TalkCheckDrainService"/>) and
    /// <see cref="ITalkCheckStore"/> over a lazy station_svc data source (the <see cref="UtteranceServiceCollectionExtensions"/>
    /// shape), and binds/validates <see cref="TalkCheckOptions"/> (<c>TalkRules:CheckRetentionDays</c>).
    /// </summary>
    public static IServiceCollection AddTalkCheckLog(
        this IServiceCollection services, string connectionString, IConfiguration configuration)
    {
        services
            .AddOptions<TalkCheckOptions>()
            .Bind(configuration.GetSection(TalkCheckOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One repository behind both seams: the write/replay store and the rules API's flag reader.
        services.AddSingleton(sp =>
            new TalkCheckRepository(
                new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build()),
                sp.GetRequiredService<IOptionsMonitor<TalkCheckOptions>>()));
        services.AddSingleton<ITalkCheckStore>(sp => sp.GetRequiredService<TalkCheckRepository>());
        services.AddSingleton<ITalkFlagReader>(sp => sp.GetRequiredService<TalkCheckRepository>());

        services.AddSingleton(Channel.CreateBounded<TalkCheckRecord>(
            new BoundedChannelOptions(QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait }));
        services.AddSingleton(sp => sp.GetRequiredService<Channel<TalkCheckRecord>>().Reader);
        services.AddSingleton(sp => sp.GetRequiredService<Channel<TalkCheckRecord>>().Writer);

        services.AddSingleton<ITalkCheckLog, TalkCheckLog>();
        services.AddHostedService<TalkCheckDrainService>();

        return services;
    }
}
