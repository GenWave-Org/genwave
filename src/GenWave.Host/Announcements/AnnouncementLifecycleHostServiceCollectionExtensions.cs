using System.Threading.Channels;

namespace GenWave.Host.Announcements;

/// <summary>
/// DI wiring for the announcement lifecycle guardians (SPEC F143.2/.3, F144.5/.6; STORY-358; PLAN
/// T343): the aired queue/sink/drain trio
/// (<see cref="AnnouncementAiredEventSink"/>/<see cref="AnnouncementAiredDrainService"/>)
/// and the periodic sweep loop (<see cref="AnnouncementLifecycleGuardianService"/>). Deliberately its
/// own extension, not folded into <c>GenWave.Host.Playout.PlayoutServiceCollectionExtensions</c>: this
/// feature area already owns its own namespace and its own registration story belongs beside it —
/// <c>AddGenWavePlayout</c> only reaches in far enough to resolve the aired sink below into its own
/// <c>CompositeStationEventSink</c> list.
/// </summary>
static class AnnouncementLifecycleHostServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveAnnouncementLifecycle(this IServiceCollection services)
    {
        // The aired-confirmation queue (SPEC F143.3, F202.1). Unbounded, deliberately: a dropped
        // signal here is a lost aired stamp — the row stays claimed and the guardian re-arms it a
        // full ReArmGrace (6 minutes) later, re-airing an announcement that already aired once. A
        // bound would only ever risk trading that outcome for memory this producer never actually
        // consumes — at most 2 announcements vend per unit (SPEC F144.1), so growth here tracks real
        // station traffic, not any cap this channel could impose. TryWrite is still the only writer
        // (never WriteAsync) — see BoothLogServiceCollectionExtensions' own identical remarks one seam
        // over.
        var airedChannel = Channel.CreateUnbounded<AnnouncementAiredSignal>(
            new UnboundedChannelOptions { SingleReader = true });
        services.AddSingleton(airedChannel.Reader);
        services.AddSingleton(airedChannel.Writer);

        services.AddSingleton<AnnouncementAiredEventSink>();

        services.AddHostedService<AnnouncementAiredDrainService>();
        services.AddHostedService<AnnouncementLifecycleGuardianService>();

        return services;
    }
}
