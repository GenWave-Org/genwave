using System.Threading.Channels;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GenWave.Core.Abstractions;
using GenWave.Loudness;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Enrich;
using GenWave.MediaLibrary.ExplicitClassification;
using GenWave.MediaLibrary.Garden;
using GenWave.MediaLibrary.Mood;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Scan;
using GenWave.MediaLibrary.Station;
using GenWave.MediaLibrary.YearLookup;
using Npgsql;

namespace GenWave.MediaLibrary;

/// <summary>
/// Composition of the in-process media library service (PRD §10). The host wires the whole service
/// with one call; extracting the library to its own process later means giving it its own host and
/// swapping the <see cref="IMediaCatalog"/> binding from this in-proc repository to an HTTP client —
/// nothing upstream moves.
/// </summary>
public static class MediaLibraryServiceCollectionExtensions
{
    public static IServiceCollection AddMediaLibrary(this IServiceCollection services, IConfiguration configuration)
    {
        // snake_case columns -> PascalCase row props (e.g. duration_ms -> DurationMs).
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        // Dapper has no built-in DateOnly parameter binding (PLAN T258 review MF2 — see
        // Station.DateOnlyTypeHandler's own remarks); station.schedule_special.on_date is this
        // codebase's first DateOnly-typed column. Registered here, unconditionally, rather than on
        // Station.SpecialsRepository's own construction: this call runs unconditionally at Host
        // startup regardless of which stores end up with a live consumer (PLAN T259 wired
        // SpecialsController as this store's first Host call site — the store no longer ships dark —
        // but the registration was never conditioned on that in the first place), so a registration
        // gated on the repository's own construction would still be the wrong ordering to depend on.
        // Global/process-wide the same way MatchNamesWithUnderscores just above is.
        SqlMapper.AddTypeHandler(DateOnlyTypeHandler.Instance);

        var connectionString = configuration.GetConnectionString("Library")
            ?? throw new InvalidOperationException("Missing connection string 'Library'.");

        services.Configure<LibraryOptions>(configuration.GetSection(LibraryOptions.Section));

        // Event-seam default (gitea-#246): the library's publishers (repositories, EnrichmentService)
        // resolve IStationEventSink; TryAdd so a host that binds a real sink (AddGenWavePlayout's
        // plain Add) wins, while a bare AddMediaLibrary container still resolves.
        services.TryAddSingleton<IStationEventSink, NoOpStationEventSink>();

        // The library owns its own data source, built from its own (library_svc) connection string —
        // the data-separation discipline expressed in code (PRD §9/§10). The role's search_path is
        // pinned to `library`, so this connection can only ever see the library schema.
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());

        // Bounded delta queue: discovery writes media ids, the enrichment workers drain them (PRD §5.2).
        // Bounded so a cold-start flood applies backpressure to the (single-flight) scan rather than
        // ballooning memory.
        services.AddSingleton(_ => Channel.CreateBounded<long>(
            new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.Wait }));

        // One repository instance, exposed as the read seam and used concretely for discovery/enrichment writes.
        services.AddSingleton<MediaRepository>();
        services.AddSingleton<IMediaCatalog>(sp => sp.GetRequiredService<MediaRepository>());
        // Admin-only unscoped lookup for object-level authorization checks (T042).
        services.AddSingleton<IAdminMediaLookup>(sp => sp.GetRequiredService<MediaRepository>());
        // Admin-only paged list returning the richer AdminMediaDto projection (T048).
        services.AddSingleton<IAdminMediaQuery>(sp => sp.GetRequiredService<MediaRepository>());
        // Admin-only sparse write: PATCH tags + eligibility with optimistic concurrency (W2).
        services.AddSingleton<IAdminMediaWrite>(sp => sp.GetRequiredService<MediaRepository>());
        // Admin-only re-enrichment scheduling: sentinel reset + bulk reset (Epic J, STORY-051).
        services.AddSingleton<IAdminMediaReenrichment>(sp => sp.GetRequiredService<MediaRepository>());
        // Authored-insert seam: lands a generated safe-segment artifact ready, no enricher round-trip
        // (F27.1/F27.2/F27.8, STORY-076). No consumer yet — P5 wires SafeSegmentAuthor onto this.
        services.AddSingleton<IAuthoredCatalogWriter>(sp => sp.GetRequiredService<MediaRepository>());
        // Operator explicit-classification override (SPEC F95.3, STORY-251, PLAN T115): its own
        // seam, kept off IAdminMediaWrite so this one method costs zero blast radius on that
        // interface's existing test doubles. First consumer: ExplicitOverrideController.
        services.AddSingleton<IMediaExplicitOverride>(sp => sp.GetRequiredService<MediaRepository>());
        // Explicit operator purge for long-unavailable rows (gh-#113): its own seam, kept off
        // IAdminMediaWrite for the same zero-blast-radius reason as IMediaExplicitOverride above.
        // First consumer: MediaPurgeController.
        services.AddSingleton<IMediaPurge>(sp => sp.GetRequiredService<MediaRepository>());

        // Rating: the operator taste signal on any catalog row (SPEC F33), standalone from
        // curation (F33.7) — no LibraryScope gating anywhere in this seam (F33.5). Same
        // library_svc NpgsqlDataSource as MediaRepository, registered the same way. First
        // consumer: RatingController (STORY-112).
        services.AddSingleton<MediaRatingRepository>();
        services.AddSingleton<IMediaRating>(sp => sp.GetRequiredService<MediaRatingRepository>());

        // The Library Gardener's rotation ledger (SPEC F149.1-F149.3, STORY-367, PLAN T355,
        // gh-#529): same library_svc NpgsqlDataSource as MediaRatingRepository just above, same
        // ISafeScopeProvider (gh-#99, bound by Host's AddGenWaveStationOptions — resolved lazily,
        // so registration order doesn't matter). Its GetRotationSinceAsync/GetNeverAiredCountAsync
        // read Gardener:RotationSince through a SECOND, dedicated StationSettingsRepository
        // instance, composed INSIDE this factory rather than registered as its own container-wide
        // singleton (T355 review MED finding: a bare public StationSettingsRepository singleton
        // would be this library extension's first station-schema wiring — its own remarks above
        // say this module owns only its own library data source — and a last-registration-wins
        // hazard for any other future station-schema consumer). MediaRotationRepository's own
        // remarks explain why a second instance is needed at all: library_svc has no grant into
        // station.settings, and this key must never enter StationSettingsAllowlist/
        // IStationSettingsStore — the SafeLoopSeedMarkerStore/AnnounceTokenStore precedent,
        // F27.10. Station connection string degrades to empty the same way every other
        // station-schema store's own registration does (SafeLoopSeedServiceCollectionExtensions,
        // AdminApiServiceCollectionExtensions) — a station DB unreachable at boot never prevents
        // this container from building; GetRotationSinceAsync simply fails when actually called.
        services.AddSingleton(sp => new MediaRotationRepository(
            sp.GetRequiredService<NpgsqlDataSource>(),
            new StationSettingsRepository(configuration.GetConnectionString("Station") ?? string.Empty),
            sp.GetRequiredService<ISafeScopeProvider>()));
        services.AddSingleton<IMediaRotationSink>(sp => sp.GetRequiredService<MediaRotationRepository>());

        // The Gardener's own boot-validated knobs (SPEC F155.1, STORY-380, PLAN T357, gh-#529) —
        // section "Gardener", top-level properties so ValidateDataAnnotations() genuinely enforces
        // every [Range] at boot (the AnnouncementsOptions "top-level binds, nested don't" shape).
        // Bound HERE, once, so MediaLibrary's own gardener passes/thumb writes (T365/T372) and the
        // Host's thumbs route limiter (T366) resolve the SAME IOptions<GardenerOptions> instance
        // rather than two independently-bound copies of one config section.
        services
            .AddOptions<GardenerOptions>()
            .Bind(configuration.GetSection(GardenerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The Library Gardener's taste-thumb store (SPEC F150.1, F150.7, F150.9; STORY-371,
        // STORY-369; PLAN T365, gh-#529): same library_svc NpgsqlDataSource + ISafeScopeProvider as
        // MediaRotationRepository just above, plus the GardenerOptions binding immediately above it
        // (HalfLifeDays/Saturation/ThumbRetentionDays) — a plain constructor-injected singleton
        // (unlike MediaRotationRepository, this type needs no second, hand-built cross-schema
        // dependency, so no factory lambda is required).
        services.AddSingleton<MediaThumbRepository>();
        services.AddSingleton<IThumbStore>(sp => sp.GetRequiredService<MediaThumbRepository>());

        // The Library Gardener's self-healing findings queue (SPEC F153.1-F153.3, F153.9;
        // STORY-374, STORY-375; PLAN T372, gh-#529): same library_svc NpgsqlDataSource as
        // MediaRotationRepository/MediaThumbRepository just above — a plain constructor-injected
        // singleton, no cross-schema dependency needed here.
        services.AddSingleton<RotFindingRepository>();
        services.AddSingleton<IRotFindingStore>(sp => sp.GetRequiredService<RotFindingRepository>());

        // The push guard's own report seam (SPEC F153.4; STORY-375; PLAN T373, gh-#529):
        // Host.Engine.MediaExistencePushGuard's fire-and-forget hook after declining a push for a
        // missing file, reporting straight through to IRotFindingStore.OpenDeadFileAsync just
        // registered above — near-instant dead_file visibility instead of waiting on the scan's
        // own state-based reconcile.
        services.AddSingleton<IDeadFileReporter, DeadFileReporter>();

        // The dead_file pass (SPEC F153.3, PLAN T372) — first of the five Gardener passes
        // ARCHITECTURE.md names; the other three (stale_metadata, shelf_dust, unreachable) join
        // this AddSingleton<IGardenerPass, ...> fan-out at their own tasks, each resolved through
        // GardenerService's own IEnumerable<IGardenerPass> in registration order.
        services.AddSingleton<IGardenerPass, DeadFileGardenerPass>();

        // The near_duplicate pass (SPEC F153.5, PLAN T374) — second of the five, registered
        // immediately after dead_file so DI registration order matches ARCHITECTURE.md's own pass
        // ordering (dead_file, near_duplicate, stale_metadata, shelf_dust, unreachable).
        services.AddSingleton<IGardenerPass, NearDuplicateGardenerPass>();

        // The tick itself (SPEC F153.2): housekeeping (IThumbStore.RecomputeAllAsync/SweepAsync,
        // F150.9) then every registered IGardenerPass, log-and-continue per step — the
        // EnrichmentService bounded-batch backfill-loop shape, one seam over.
        services.AddHostedService<GardenerService>();

        // gh-#99: the narrow cross-schema membership answer the taste-thumb/booth-log surfaces
        // need — resolved on the library connection because station_svc deliberately has no grant
        // on library.media.
        services.AddSingleton<IMediaLibraryMembership, MediaLibraryMembershipRepository>();

        // SPEC F115.4, STORY-305, PLAN T240: the show delete guard's own cross-schema answer — which
        // library.media rows are scoped to a show, and clearing them — same station_svc-has-no-grant
        // rationale as IMediaLibraryMembership immediately above. First consumer: ShowsController.
        services.AddSingleton<IShowImagingScope, ShowImagingScopeRepository>();

        // SPEC F87.5, STORY-226, PLAN T89: the listener-request matcher's catalog probe — same
        // cross-schema-boundary rationale as IMediaLibraryMembership above (station_svc has no grant
        // on library.media). First consumer: GenWave.Host.Requests.RequestMatcher.
        services.AddSingleton<IRequestCatalogProbe, RequestCatalogProbeRepository>();

        // Artwork token seam (SPEC F88.2, gh-#105, STORY-222): lazy per-track token generation +
        // token→media resolution. No consumer yet — T84 wires GET /spectator/api/artwork/{token}
        // onto this.
        services.AddSingleton<IArtworkTokenStore, ArtworkTokenRepository>();

        // Library read: name lookup + all-libraries-with-count for the admin library list endpoint.
        services.AddSingleton<ILibraryRepository, LibraryRepository>();
        // Library admin write: create/rename/delete (Epic J, STORY-047).
        services.AddSingleton<IAdminLibraryWrite, AdminLibraryRepository>();

        services.Configure<CueDetectionOptions>(configuration.GetSection(CueDetectionOptions.Section));
        services.Configure<EnergyOptions>(configuration.GetSection(EnergyOptions.Section));

        services.AddSingleton<ILoudnessAnalyzer, FfmpegLoudnessAnalyzer>();
        services.AddSingleton<ICueAnalyzer, FfmpegCueAnalyzer>();
        services.AddSingleton<IEnergyAnalyzer, FfmpegEnergyAnalyzer>();
        // The fourth sibling analyzer (SPEC F46.1). Registered here for parity with its siblings;
        // inert until X3 wires it into Enricher/EnrichmentService's first-pass + backfill.
        services.AddSingleton<IBpmAnalyzer, AubioBpmAnalyzer>();
        services.AddSingleton<Enricher>();

        // MusicBrainz year lookup (SPEC F48.1-F48.2, closes gitea-#208). No boot-frozen BaseAddress —
        // Library:YearLookup:Endpoint is read from IOptionsMonitor<YearLookupOptions>.CurrentValue
        // per call inside MusicBrainzYearLookup, so a live PUT applies to the next lookup with no
        // api restart (the same F36.2 shape as KokoroTtsSynthesizer's own typed client). Registered
        // here (unlike Tts's Program.cs wiring) because MediaLibrary owns its own composition root.
        // Inert until X5 wires IYearLookup into EnrichmentService's backfill claim loop; the DI graph
        // resolves regardless, so Host boot is unaffected. MaxResponseContentBufferSize bounds a
        // recording-search reply (review finding, mirrors LlmCopyWriter's own Program.cs bound) — a
        // misbehaving/compromised endpoint can't make this client buffer an unbounded response body.
        //
        // TimeProvider.System / MusicBrainzRateLimiter (SPEC F76.1): one rate limiter for the whole
        // process — TryAdd so a host or test that already registers its own TimeProvider wins (the
        // same GenWave.Tts/GenWave.Orchestration precedent).
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<MusicBrainzRateLimiter>();
        services.Configure<YearLookupOptions>(configuration.GetSection(YearLookupOptions.Section));
        services.AddHttpClient<MusicBrainzYearLookup>(client =>
        {
            client.MaxResponseContentBufferSize = MusicBrainzYearLookup.MaxResponseContentBytes;
        });
        services.AddSingleton<IYearLookup>(sp => sp.GetRequiredService<MusicBrainzYearLookup>());

        // Mood tagger (SPEC F85.2-F85.4, STORY-216, T72). Same "no boot-frozen BaseAddress" shape as
        // MusicBrainzYearLookup above — MoodTaggerOptions is read fresh via IOptionsMonitor per call.
        // Bound to the SAME "Llm" config section GenWave.Tts's own LlmOptions binds (see
        // MoodTaggerOptions' own remarks for why this is a second options class, not a cross-module
        // reference). Inert until GenWave.Host also registers ILlmBatchGate (over Tts's degradation
        // reader, which MediaLibrary must never reference) — EnrichmentService's mood-tag backfill
        // treats either dependency being absent as a no-op, so the DI graph resolves regardless and
        // Host boot is unaffected either way.
        services.Configure<MoodTaggerOptions>(configuration.GetSection(MoodTaggerOptions.Section));
        services.AddHttpClient<OllamaMoodTagger>(client =>
        {
            client.MaxResponseContentBufferSize = OllamaMoodTagger.MaxResponseContentBytes;
        });
        services.AddSingleton<IMoodTagger>(sp => sp.GetRequiredService<OllamaMoodTagger>());

        // Explicit classification sweep (SPEC F95.3, STORY-251, T113) — the exact same shape as the
        // mood tagger immediately above, one column pair later: its own options class bound to the
        // SAME "Llm" section (ExplicitClassifierOptions' own remarks explain why), no boot-frozen
        // BaseAddress, MaxResponseContentBufferSize bounded the same way. Inert until GenWave.Host
        // also registers ILlmBatchGate (already required by the mood tagger above, so a host wiring
        // one wires both) — EnrichmentService's explicit-classification backfill treats either
        // dependency being absent as a no-op, so the DI graph resolves regardless and Host boot is
        // unaffected either way.
        services.Configure<ExplicitClassifierOptions>(configuration.GetSection(ExplicitClassifierOptions.Section));
        services.AddHttpClient<OllamaExplicitClassifier>(client =>
        {
            client.MaxResponseContentBufferSize = OllamaExplicitClassifier.MaxResponseContentBytes;
        });
        services.AddSingleton<IExplicitClassifier>(sp => sp.GetRequiredService<OllamaExplicitClassifier>());

        // Scan availability grace (SPEC F58, closes gitea-#223) — Library:Scan:MissThreshold read fresh
        // per tick via IOptionsMonitor<ScanOptions>, the same F44.2 shape as Library:ScanIntervalSeconds
        // above; a live PUT governs the very next tick's missing-diff, no api restart.
        services.Configure<ScanOptions>(configuration.GetSection(ScanOptions.Section));

        services.AddHostedService<ScanService>();
        services.AddHostedService<EnrichmentService>();

        return services;
    }
}
