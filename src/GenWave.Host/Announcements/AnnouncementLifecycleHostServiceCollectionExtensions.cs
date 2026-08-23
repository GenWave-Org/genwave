using System.Threading.Channels;

namespace GenWave.Host.Announcements;

/// <summary>
/// DI wiring for the announcement lifecycle guardians (SPEC F143.2/.3, F144.5/.6, F145.2; STORY-358,
/// STORY-359; PLAN T343): the two queue/sink/drain pairs
/// (<see cref="AnnouncementAiredEventSink"/>/<see cref="AnnouncementAiredDrainService"/>,
/// <see cref="AnnouncementPrivacyFlipEventSink"/>/<see cref="AnnouncementPrivacyFlipDrainService"/>)
/// and the periodic sweep loop (<see cref="AnnouncementLifecycleGuardianService"/>). Deliberately its
/// own extension, not folded into <c>GenWave.Host.Playout.PlayoutServiceCollectionExtensions</c>: this
/// feature area already owns its own namespace (<c>SpectatorModeAnnouncementVendGuard</c>) and its own
/// registration story belongs beside it — <c>AddGenWavePlayout</c> only reaches in far enough to
/// resolve the two sinks below into its own <c>CompositeStationEventSink</c> list.
/// </summary>
static class AnnouncementLifecycleHostServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveAnnouncementLifecycle(this IServiceCollection services)
    {
        // The aired-confirmation queue (SPEC F143.3). Bounded small: at most 2 announcements vend
        // per unit (SPEC F144.1), so a backlog here would already mean dozens of units aired between
        // drain ticks — a bound this station's real traffic never approaches, existing only to cap
        // memory against a genuinely pathological DB outage. TryWrite (never WriteAsync) is the only
        // writer, so FullMode never blocks the publishing sink — see BoothLogServiceCollectionExtensions'
        // own identical remarks one seam over.
        var airedChannel = Channel.CreateBounded<AnnouncementAiredSignal>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait });
        services.AddSingleton(airedChannel.Reader);
        services.AddSingleton(airedChannel.Writer);

        // The privacy-flip queue (SPEC F145.2) — a settings write is a rare, human-driven event;
        // capacity 4 is generous headroom, never a real limit.
        var flipChannel = Channel.CreateBounded<AnnouncementPrivacyFlipSignal>(
            new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.Wait });
        services.AddSingleton(flipChannel.Reader);
        services.AddSingleton(flipChannel.Writer);

        services.AddSingleton<AnnouncementAiredEventSink>();
        services.AddSingleton<AnnouncementPrivacyFlipEventSink>();

        services.AddHostedService<AnnouncementAiredDrainService>();
        services.AddHostedService<AnnouncementPrivacyFlipDrainService>();
        services.AddHostedService<AnnouncementLifecycleGuardianService>();

        return services;
    }
}
