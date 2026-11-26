using GenWave.Core.TalkRules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>DI wiring for the talk-rule switch store (SPEC F220.22, F220.23, STORY-571).</summary>
public static class TalkRuleSwitchServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Postgres <see cref="ITalkRuleSwitches"/> over a lazy station_svc data source. Uses Replace so
    /// it wins over the in-memory stand-in whichever side of <c>AddTalkRules</c> it is called on.
    /// </summary>
    public static IServiceCollection AddTalkRuleSwitchStore(this IServiceCollection services, string connectionString) =>
        services.Replace(ServiceDescriptor.Singleton<ITalkRuleSwitches>(_ =>
            new TalkRuleSwitchRepository(
                new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build()))));
}
