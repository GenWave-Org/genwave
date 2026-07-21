using System.Threading.Channels;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;
using GenWave.Loudness;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Enrich;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Scan;
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

        // Rating: the operator taste signal on any catalog row (SPEC F33), standalone from
        // curation (F33.7) — no LibraryScope gating anywhere in this seam (F33.5). Same
        // library_svc NpgsqlDataSource as MediaRepository, registered the same way. First
        // consumer: RatingController (STORY-112).
        services.AddSingleton<MediaRatingRepository>();
        services.AddSingleton<IMediaRating>(sp => sp.GetRequiredService<MediaRatingRepository>());

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

        // Scan availability grace (SPEC F58, closes gitea-#223) — Library:Scan:MissThreshold read fresh
        // per tick via IOptionsMonitor<ScanOptions>, the same F44.2 shape as Library:ScanIntervalSeconds
        // above; a live PUT governs the very next tick's missing-diff, no api restart.
        services.Configure<ScanOptions>(configuration.GetSection(ScanOptions.Section));

        services.AddHostedService<ScanService>();
        services.AddHostedService<EnrichmentService>();

        return services;
    }
}
