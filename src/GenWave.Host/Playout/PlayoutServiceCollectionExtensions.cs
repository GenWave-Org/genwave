using System.Threading.Channels;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Host.Announcements;
using GenWave.Host.Artwork;
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
            // The on-air render in-flight signal (SPEC F127.7, PLAN T286 review F1) — set by
            // PlayoutFeederService around its own feeder.RefillAsync call below, read by
            // CrosstalkStockWorker (GenWave.Host.Crosstalk) so it never generates while a real
            // on-air LLM+TTS render is running. See OnAirRenderGate's own remarks.
            .AddSingleton<OnAirRenderGate>()
            .AddSingleton<PlayHistoryEventSink>()
            // Crosstalk retire-at-air (SPEC F127.7, STORY-329, PLAN T287) — auto-constructor-resolved
            // (no factory), so its own optional CrosstalkPlanner? ctor param degrades to null (a
            // harmless no-op) whenever GenWave.Host.Crosstalk.CrosstalkHostServiceCollectionExtensions
            // was never called, with no ordering dependency on when it IS. See
            // CrosstalkRetirementEventSink's own remarks.
            .AddSingleton<CrosstalkRetirementEventSink>()
            // The rotation ledger (SPEC F149.2, STORY-367, PLAN T355, gh-#529) — mirrors the
            // announcement lifecycle's own queue/sink/drain shape
            // (GenWave.Host.Announcements.AnnouncementLifecycleHostServiceCollectionExtensions) one
            // seam over, wired HERE rather than its own extension: CONTRIBUTING.md L5 keeps every
            // sink member — and its queue/drain pair — where the rest of this file's own TrackAired
            // consumers already live, never under a GenWave.Host.Gardener namespace. Bounded 512,
            // the same "every track start" cadence bound BoothLogWriter's own queue already uses
            // (that class's own remarks) — every music airing writes here, at most one per advance.
            .AddSingleton(_ => Channel.CreateBounded<MediaRotationAiredSignal>(
                new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.Wait }))
            .AddSingleton(sp => sp.GetRequiredService<Channel<MediaRotationAiredSignal>>().Reader)
            .AddSingleton(sp => sp.GetRequiredService<Channel<MediaRotationAiredSignal>>().Writer)
            .AddSingleton<MediaRotationEventSink>()
            .AddHostedService<MediaRotationDrainService>()
            // The host's event-sink binding (gitea-#246): composes PlayHistoryEventSink
            // (TrackAired -> play history ring), the booth log's BoothLogWriter
            // (IBoothLogEventConsumer, SPEC F72.1, STORY-195), CrosstalkRetirementEventSink
            // (TrackAired -> asset delete, PLAN T287), the announcement lifecycle guardians' own
            // two sinks (AnnouncementAiredEventSink — TrackAired -> aired stamp, SPEC F143.3;
            // AnnouncementPrivacyFlipEventSink — SettingChanged -> decline sweep, SPEC F145.2; both
            // PLAN T343, GenWave.Host.Announcements), and MediaRotationEventSink (TrackAired ->
            // rotation ledger upsert, SPEC F149.2, PLAN T355) into the ONE binding every publisher
            // resolves — see CompositeStationEventSink's own remarks. Deliberately a plain Add (not
            // TryAdd) so it wins over the no-op defaults the library extensions register; a future
            // consumer is added to the list this factory builds, not by re-wiring any existing sink.
            // Both announcement sinks are auto-constructor-resolved with no factory, exactly like
            // CrosstalkRetirementEventSink just above — GenWave.Host.Announcements.AnnouncementLifecycleHostServiceCollectionExtensions
            // registers them (and the channels their constructors need) independently, with no ordering
            // dependency on when it runs relative to this call (Program.cs happens to call it after,
            // but nothing here requires that — see CrosstalkRetirementEventSink's own remarks for why
            // that holds for a lazily-resolved DI singleton regardless). MediaRotationEventSink is
            // registered by THIS same call, so no such ordering question exists for it at all.
            .AddSingleton<IStationEventSink>(sp => new CompositeStationEventSink(
                [
                    sp.GetRequiredService<PlayHistoryEventSink>(),
                    sp.GetRequiredService<IBoothLogEventConsumer>(),
                    sp.GetRequiredService<CrosstalkRetirementEventSink>(),
                    sp.GetRequiredService<AnnouncementAiredEventSink>(),
                    sp.GetRequiredService<AnnouncementPrivacyFlipEventSink>(),
                    sp.GetRequiredService<MediaRotationEventSink>(),
                ],
                sp.GetRequiredService<ILogger<CompositeStationEventSink>>()))
            // Persona-id -> worn-face token, memoized on a ≤30s TTL (SPEC F129.5, STORY-336, PLAN
            // T300, gh-#482 rider) — the ONE shared memo both ArtworkUrlResolver (below) and
            // SpectatorController (Host.Api) read, so the ICY stream and the now-playing payload
            // can never answer a stale-vs-fresh token differently for the same instant. Depends only
            // on IPersonaAvatarStore (bound by AddGenWaveStationSettings, which Program.cs runs
            // before this) and TimeProvider.
            .AddSingleton<PersonaAvatarTokenCache>()
            // The station image's own ≤30s-memoized read (SPEC F131.2/F131.3, STORY-339, PLAN T307
            // review rider, MANDATORY) — the ONE shared memo every station-image reader goes
            // through: this push path (ArtworkUrlResolver, below), the spectator artwork fallback
            // ladder and station-token route (SpectatorArtworkController), and the spectator
            // favicon/logo routes (SpectatorPageEndpoints) — so an anonymous miss at the spectator
            // surface's own 120/min/IP rate limit can never drag the full ~200 KiB bytes column
            // through Postgres per request. Depends only on IStationImageStore (bound by
            // AddGenWaveStationSettings, which Program.cs runs before this) and TimeProvider —
            // mirrors PersonaAvatarTokenCache's own registration just above one-for-one.
            .AddSingleton<StationImageCache>()
            // Artwork/station/dj-token URL resolution on the push path (SPEC F88.4–F88.5,
            // STORY-223, PLAN T85; amended F129.4, STORY-336, PLAN T300; amended F131.2, STORY-339,
            // PLAN T307) — shared by LiquidsoapControl.PushAsync and the safe-track endpoint
            // (InternalEndpoints), mirroring how LiquidsoapAnnotationBuilder itself is shared
            // between the two. Depends on IOptionsMonitor<StationOptions> (bound by
            // AddGenWaveStationOptions), IArtworkTokenStore (bound by AddMediaLibrary),
            // IActivePersonaAccessor (bound by AddGenWaveStationSettings), PersonaAvatarTokenCache,
            // and StationImageCache (both just above) — Program.cs runs all of these before
            // AddGenWavePlayout.
            .AddSingleton<ArtworkUrlResolver>()
            // The engine-control seam, bound to the configured Liquidsoap host. Station name on
            // the push path is read live through IStationIdentityProvider (SPEC F44.1). Wrapped in
            // the push-honesty guard (gh-#612): a push whose file is already gone from disk is
            // declined with a WARN here, api-side, instead of dying silently at engine-side
            // resolution behind a success-shaped RID reply — see MediaExistencePushGuard's remarks.
            .AddSingleton<ILiquidsoapControl>(sp => new MediaExistencePushGuard(
                new LiquidsoapControl(
                    sp.GetRequiredService<IOptions<LiquidsoapOptions>>().Value,
                    SingleStation.IdString,
                    sp.GetRequiredService<IStationIdentityProvider>(),
                    sp.GetRequiredService<ArtworkUrlResolver>(),
                    sp.GetRequiredService<ILogger<LiquidsoapControl>>()),
                sp.GetRequiredService<ILogger<MediaExistencePushGuard>>()))
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
                    sp.GetRequiredService<IStationEventSink>(),
                    // Same ArtworkUrlResolver instance the push path already resolves through — it
                    // doubles as IArtworkUrlEchoValidator (PLAN T125 review F2) so both directions
                    // share one PublicBaseUrl/path composition (see that type's own remarks).
                    sp.GetRequiredService<ArtworkUrlResolver>());
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
                    sp.GetRequiredService<NowPlayingService>(),
                    sp.GetRequiredService<OnAirRenderGate>());
            })
            // PlayoutSupervisor runs the single station's feeder, bound to the configured engine host.
            .AddHostedService<PlayoutSupervisor>();
}
