using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Host.Engine;
using GenWave.Host.Options;

namespace GenWave.Host.Playout;

/// <summary>
/// The playout chain in DI (gitea-#243 — previously hand-wired inside <see cref="PlayoutSupervisor"/>):
/// engine control → feeder → feeder service → supervisor. Everything is a singleton; the
/// supervisor is the lone <c>IHostedService</c> that starts and stops the feeder service.
/// </summary>
static class PlayoutServiceCollectionExtensions
{
    public static IServiceCollection AddGenWavePlayout(this IServiceCollection services) =>
        services
            .AddSingleton<PlayHistoryService>()
            // Duration rehydration (SPEC F66.2-F66.4): NowPlayingService's optional ctor dependency,
            // resolved here so every Update() call can trigger it. Depends only on IMediaCatalog
            // (bound by AddMediaLibrary) and PlayHistoryService — no dependency back on
            // NowPlayingService itself, so there is no DI cycle.
            .AddSingleton<DurationRehydrator>()
            .AddSingleton<NowPlayingService>()
            // The host's event-sink binding (gitea-#246): TrackAired → play history; everything else
            // no-op. Deliberately a plain Add (not TryAdd) so it wins over the no-op defaults the
            // library extensions register; a module replaces or decorates THIS binding.
            .AddSingleton<IStationEventSink, PlayHistoryEventSink>()
            // The engine-control seam, bound to the configured Liquidsoap host. Station name on
            // the push path is read live through IStationIdentityProvider (SPEC F44.1).
            .AddSingleton<ILiquidsoapControl>(sp => new LiquidsoapControl(
                sp.GetRequiredService<IOptions<LiquidsoapOptions>>().Value,
                SingleStation.IdString,
                sp.GetRequiredService<IStationIdentityProvider>(),
                sp.GetRequiredService<ILogger<LiquidsoapControl>>()))
            // Loudness target/ceiling are deliberate boot-time values (engine-side knobs apply on
            // restart) — snapshot IOptions, not a live monitor.
            .AddSingleton(sp =>
            {
                var loudness = sp.GetRequiredService<IOptions<LoudnessOptions>>().Value;
                return new PlayoutFeeder(
                    sp.GetRequiredService<ILiquidsoapControl>(),
                    sp.GetRequiredService<INextItemProvider>(),
                    sp.GetRequiredService<IRotationSettingsProvider>(),
                    loudness.TargetLufs,
                    loudness.CeilingDbtp,
                    sp.GetRequiredService<IStationEventSink>());
            })
            .AddSingleton(sp =>
            {
                // A one-time boot snapshot for the Station record's shape only — every RECURRING
                // name use (the engine push path, the feeder's tick logs) reads
                // IStationIdentityProvider live instead (SPEC F44.1, gitea-#196), never this snapshot.
                //
                // The single station, assembled from config. EngineHost is the configured
                // Liquidsoap host; ListenerFqdn/IcecastHost/Cadence are unused on the feeder path.
                // Cadence is a placeholder default: Station.Cadence is dead weight (nothing
                // downstream ever reads it) — wiring ICadenceProvider in here would only read
                // .Current once at construction, an inert re-creation of the exact boot-freeze
                // gitea-#211 fixes elsewhere. Deleting Station.Cadence (and its unused
                // ListenerFqdn/IcecastHost siblings) is follow-up scope (gitea-#206).
                var identityProvider = sp.GetRequiredService<IStationIdentityProvider>();
                var station = new Station(
                    SingleStation.Id,
                    identityProvider.Current.Name,
                    ListenerFqdn: "",
                    EngineHost: sp.GetRequiredService<IOptions<LiquidsoapOptions>>().Value.Host,
                    IcecastHost: "",
                    new CadenceConfig(),
                    DateTimeOffset.UtcNow);
                return new PlayoutFeederService(
                    station,
                    sp.GetRequiredService<PlayoutFeeder>(),
                    identityProvider,
                    sp.GetRequiredService<ILogger<PlayoutFeederService>>(),
                    sp.GetRequiredService<NowPlayingService>());
            })
            // PlayoutSupervisor runs the single station's feeder, bound to the configured engine host.
            .AddHostedService<PlayoutSupervisor>();
}
