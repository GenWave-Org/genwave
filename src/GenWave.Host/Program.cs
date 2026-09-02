using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GenWave.Core.Abstractions;
using GenWave.Host.Announcements;
using GenWave.Host.Api;
using GenWave.Host.Artwork;
using GenWave.Host.Catalog;
using GenWave.Host.Configuration;
using GenWave.Host.Crosstalk;
using GenWave.Host.Enrichment;
using GenWave.Host.Health;
using GenWave.Host.Images;
using GenWave.Host.Options;
using GenWave.Host.Playout;
using GenWave.Host.Pronunciations;
using GenWave.Host.Requests;
using GenWave.Host.Seeding;
using GenWave.Host.Stats;
using GenWave.Host.Theming;
using GenWave.MediaLibrary;
using GenWave.MediaLibrary.Options;
using GenWave.Orchestration;
using GenWave.Tts;
using Microsoft.Extensions.Options;

// Composition root for the GenWave control plane — SINGLE STATION. One deployment broadcasts one
// station, configured entirely from the `Station` config section (no DB-backed station registry,
// no tenancy). The feeder PULLS the next track through INextItemProvider (the Orchestrator weaves
// music + TTS patter). Uniform singleton lifetimes; the library's connection-per-query keeps the
// whole graph singleton-safe.
//
// Registrations live in cohesive AddGenWave*/Add* extensions owned by the project that owns the
// services (gitea-#243) — this file only sequences them. A future module overrides a seam by registering
// its own implementation AFTER the extension that binds the default.

var builder = WebApplication.CreateBuilder(args);

// Theme system (SPEC F102, ARCHITECTURE "Theme system", PLAN T160): the shipped manifests are
// loaded and validated ONCE here, at boot — not lazily on first request. ThemeCatalog.LoadShipped's
// own remarks say a malformed manifest is a build-time authoring bug, not a request-time condition
// to route around, so it throws; doing that here means a bad manifest stops the process before it
// ever accepts a request, rather than surfacing as a 500 to whichever visitor happens to be first.
// The extra assertion below covers a narrower case LoadShipped itself does not: both serving
// surfaces' shared ThemeCatalog.Resolve (PLAN T164) falls all the way through to
// ThemeCatalog.ShippedDefaultSlug as the floor of its precedence cascade — if that slug were ever
// renamed or dropped from the shipped set, this converts what would otherwise be a per-request
// failure (Resolve's own InvalidOperationException) into the same boot-time failure as every other
// catalog defect.
//
// Deliberately placed as the FIRST statement after builder construction — by construction, not by
// coincidence of ordering (T163 review hardening). StationSettingsAllowlist carries its own
// independent ThemeCatalog.LoadShipped() call in a static field initializer (see that class's own
// remarks) — a beforefieldinit type, whose CLR-mandated init point is merely "sometime before first
// access", not "here". If any future code between builder construction and this point ever touched
// StationSettingsAllowlist first (directly, or transitively through an extension method), a
// corrupt shipped manifest would throw a TypeInitializationException-wrapped ThemeManifestException
// instead of this clean, unwrapped one — the same failure, but the caller doing exception-type
// matching (logs, monitoring) sees a different shape. Loading here, before ANY other statement
// (including builder.AddGenWaveStationSettings() below) has a chance to run, means nothing can ever
// touch the allowlist first — the boot-time failure guarantee holds by construction rather than by
// what happens not to run yet.
// This canary result is deliberately NOT what gets registered for request handling below — it is a
// throwaway, embedded-only ThemeCatalog.LoadShipped() build that exists purely to win the ordering
// race this block's own remarks describe. The real, DI-registered ThemeCatalog (SPEC F103.7,
// STORY-271, PLAN T182) is built via ThemeCatalog.CreateForStation once IThemeStore exists
// (builder.AddGenWaveStationSettings() below registers it) — CreateForStation re-parses these same
// embedded resources, so if this canary passes, that build is guaranteed to as well.
var shippedThemeCanary = ThemeCatalog.LoadShipped();
if (!shippedThemeCanary.TryGetBySlug(ThemeCatalog.ShippedDefaultSlug, out _))
{
    throw new InvalidOperationException(
        $"shipped theme catalog is missing its own default slug '{ThemeCatalog.ShippedDefaultSlug}'");
}

// Process boot instant, captured once here — not lazily by DI on first resolution — so
// GET /api/status's startedAt (SPEC F28.6) reflects true process start.
builder.Services.AddSingleton(new ProcessStartTime(DateTimeOffset.UtcNow));

// Station settings overlay + store + persona store (ConnectionStrings:Station). Mutates
// builder.Configuration (appends the live overlay source), so it runs before anything binds options.
builder.AddGenWaveStationSettings();

// The runtime theme catalog (SPEC F103.7, STORY-271, PLAN T182): shipped ∪ owner, over the
// IThemeStore AddGenWaveStationSettings() just registered. CreateForStation itself reads only
// embedded resources (no DB call merely from resolving this singleton — the shipped-only canary
// above already proved these same resources parse clean); ThemeCatalogOwnerLoadHostedService below
// is what folds station.theme's rows in, once per boot, without blocking Kestrel from listening —
// the SPEC F102.7 offline floor holds throughout: an unreachable/empty store leaves this catalog
// serving the shipped-only set it started with.
builder.Services.AddSingleton(sp =>
    ThemeCatalog.CreateForStation(sp.GetRequiredService<IThemeStore>(), sp.GetRequiredService<ILogger<ThemeCatalog>>()));
builder.Services.AddHostedService<ThemeCatalogOwnerLoadHostedService>();

// The installed-font catalog (SPEC F104.6/F104.8, STORY-283, PLAN T200): the OTHER half of the
// widened /fonts/{file} closed set, over the IFontPackStore AddGenWaveStationSettings() just
// registered. InstalledFontCatalog.Create itself reads nothing from the store merely by being
// constructed (mirrors ThemeCatalog.CreateForStation's own "resolving is never enough to connect"
// rule) — InstalledFontCatalogLoadHostedService below is what loads station.font_pack(+_face)'s rows
// in, once per boot, without blocking Kestrel from listening; FontPackController reloads it again
// after every successful install. The SPEC F104.8 offline floor holds throughout: an
// unreachable/empty store (or one that later goes unreachable) leaves this catalog serving whatever
// installed set it last loaded successfully — vendored faces (FontEndpoints' own literal switch) are
// untouched by any of this either way.
builder.Services.AddSingleton(sp =>
    InstalledFontCatalog.Create(sp.GetRequiredService<IFontPackStore>(), sp.GetRequiredService<ILogger<InstalledFontCatalog>>()));
builder.Services.AddHostedService<InstalledFontCatalogLoadHostedService>();

var cfg = builder.Configuration;

builder.Services
    // Station/engine options + the live provider seams (identity, scope, cadence, rotation,
    // render budget).
    .AddGenWaveStationOptions(cfg)
    // The media library service: catalog (IMediaCatalog) + discovery scan + enrichment, with its
    // own data source on the dedicated library_svc connection (PRD §9/§10).
    // AddMediaLibrary also registers ILoudnessAnalyzer as a singleton (FfmpegLoudnessAnalyzer).
    .AddMediaLibrary(cfg)
    // TTS: options, copy-writer chain (LLM → template fallback), synthesizer/voices clients.
    .AddGenWaveTts(cfg)
    // The offline batch LLM passes' shared degradation gate (SPEC F85.3, F95.3; STORY-216/T72 mood
    // tagging, STORY-251/T113 explicit classification): bridges GenWave.MediaLibrary's
    // EnrichmentService to Tts's F69 degradation state without GenWave.MediaLibrary ever referencing
    // GenWave.Tts. MUST run after .AddGenWaveTts(cfg) above (IDegradationModeReader/LlmOptions).
    .AddGenWaveLlmBatchGate()
    // Listener-request wish parser (SPEC F87.4, STORY-225, PLAN T88): the channel-fed background
    // service + IWishParser pair (LLM-backed, deterministic fallback). Same ordering constraint as
    // AddGenWaveLlmBatchGate above — needs IDegradationModeReader/LlmOptions from AddGenWaveTts.
    .AddGenWaveRequestParsing()
    // Safe-loop authoring pipeline (F27): TTS render → jingle-bed mix → measure → authored insert.
    .AddGenWaveSafeSegmentAuthoring()
    // SEAM 1: the Orchestrator is the INextItemProvider (music + TTS patter interleave).
    .AddGenWaveOrchestration()
    // Real ranker-backed persona pick provider (SPEC F81.6 rung 0, F82; STORY-213, PLAN T64) —
    // MUST run after AddGenWaveOrchestration so its AddSingleton<IPersonaPickProvider> wins over
    // that call's own TryAddSingleton<..., NoOpPersonaPickProvider> default (see this extension's
    // own remarks).
    .AddGenWavePersonaRanking(cfg)
    // Real fulfillment-rung source (SPEC F87.6, STORY-227, PLAN T90) — MUST run after
    // AddGenWaveOrchestration so its AddSingleton<IRequestFulfillmentSource> wins over that call's
    // own TryAddSingleton<..., NoOpRequestFulfillmentSource> default (mirrors AddGenWavePersonaRanking's
    // own ordering rule one line above).
    .AddGenWaveRequestFulfillment()
    // The F107 context seam's Host half (SPEC F107.2-F107.7, STORY-297, PLAN T226): live
    // Options-backed IContextSettingsProvider/IStationLocationProvider/IContextCacheRootProvider,
    // the ContextPipeline singleton, its IContextPatterFactSource override, and the ContextTickerService
    // hosted service. MUST run after .AddGenWaveOrchestration() (needs SpeechDeferralQueue) and
    // .AddGenWaveTts() (needs IOptionsMonitor<TtsOptions> and must win over its own
    // TryAddSingleton<IContextPatterFactSource, NoOpContextPatterFactSource> default) — both already
    // ran above. See AddGenWaveContextHost's own remarks for the full ordering rationale.
    .AddGenWaveContextHost(cfg)
    // Playout chain: engine control → feeder → feeder service → PlayoutSupervisor (hosted).
    .AddGenWavePlayout()
    // The announcement lifecycle guardians (SPEC F143.2/.3, F144.5/.6, F145.2; STORY-358/359,
    // PLAN T343): the aired-confirmation and privacy-flip sinks/drains, plus the periodic
    // expire/re-arm sweep loop. Registered AFTER .AddGenWavePlayout() so this call's own
    // GenWave.Host.Announcements namespace narrative reads "the playout chain, then the guardians
    // that watch it" — resolution itself has no ordering dependency either way, since every
    // registration below is a lazy DI factory/hosted-service Add (see
    // AnnouncementLifecycleHostServiceCollectionExtensions' own remarks). Needs IAnnouncementLifecycle
    // (AddGenWaveStationSettings, above) and IStationEventSink's own two new sink resolutions inside
    // AddGenWavePlayout's own CompositeStationEventSink factory — both already registered by the time
    // anything actually resolves IStationEventSink.
    .AddGenWaveAnnouncementLifecycle()
    // Crosstalk stock-timer loop (SPEC F127.7, STORY-328, PLAN T286): the thin Host shell that
    // wires CrosstalkPlanner (GenWave.Orchestration) to CrosstalkScriptWriter/CrosstalkAssembler
    // (GenWave.Tts). MUST run after .AddGenWaveTts()/.AddGenWaveOrchestration() (both above) and
    // .AddGenWavePlayout() (needs NowPlayingService, just registered) — see
    // AddGenWaveCrosstalkHost's own remarks for the full ordering rationale.
    .AddGenWaveCrosstalkHost()
    // Boot seed: branded safe-loop backstop (F27.6), one-shot + idempotent.
    .AddGenWaveSafeLoopSeed(cfg)
    // Boot migration: reconciles station.persona onto the F71.1 card schema and ensures the
    // slug:"default" persona row (SPEC F71.2, STORY-192), one-shot + idempotent.
    .AddGenWavePersonaCardMigration(cfg)
    // Background dependency health probes (SPEC F70.2, STORY-187): cached Ollama/Kokoro verdicts
    // a future render-time fallback decision (T34) reads synchronously — no health check ever
    // runs inside the render window.
    .AddGenWaveDependencyHealth(cfg)
    // Admin surface: admin options, Data Protection, cookie auth, deny-by-default policy.
    .AddGenWaveAdminApi(cfg)
    // Named OutputCache policies for the public spectator surface (SPEC F62.10, STORY-171/T13).
    .AddGenWaveSpectatorOutputCaching();

// The plugin door (SPEC F156, STORY-385/386, PLAN T394) — run AFTER every AddGenWave* registration
// above, BEFORE builder.Build() (F156.8's own ordering requirement: a committed plugin's
// registrations must already be in the container the instant the host finishes building). See
// PluginDoorServiceCollectionExtensions' own remarks for the two-knob gate and the closed-door
// inertness guarantee.
builder.Services.AddGenWavePluginDoor(cfg);

// SPEC F142 (STORY-356, PLAN T327, closes gh-#300): the boundary cadence covenant's clamp-up + WARN.
// Registered HERE, not inside AddGenWaveStationOptions above (GenWave.Host.Options), because
// BoundaryCadenceCovenantPostConfigure needs PlayoutFeederService.PullInterval
// (GenWave.Host.Playout) as its worst-case feeder pull gap — Options already can't reach into
// Playout without opening the cycle gh-#445's namespace-cycle fitness law forbids (Playout depends
// on Options, never the reverse). This composition root sees both without creating one. Safe
// regardless of exactly where among these registrations it lands: IPostConfigureOptions<T>
// registrations are aggregated lazily by OptionsFactory<T> on first StationOptions bind, never
// eagerly at Add-time — see BoundaryCadenceCovenantPostConfigure's own remarks for the framework
// ordering guarantee (it always runs before every IValidateOptions<StationOptions>, including
// StationOptionsValidator, registered by AddGenWaveStationOptions above).
builder.Services.AddSingleton<IPostConfigureOptions<StationOptions>>(sp =>
    new BoundaryCadenceCovenantPostConfigure(
        sp.GetRequiredService<ILogger<BoundaryCadenceCovenantPostConfigure>>(),
        PlayoutFeederService.PullInterval));

// Public listener config (SPEC F64.1/F64.2, STORY-172): env/compose-only, deliberately absent
// from StationSettingsAllowlist — flipping Spectator:PublicPort requires a container recreate plus
// the matching compose port mapping, never a live PUT. Read live via
// IOptionsMonitor<SpectatorOptions> by SurfaceGateMiddleware.
builder.Services.Configure<SpectatorOptions>(cfg.GetSection(SpectatorOptions.SectionName));

// Icecast admin-stats listener count (SPEC F62.12 addendum, STORY-179, gitea-#10): env/compose-only,
// deliberately absent from StationSettingsAllowlist (AdminPassword is a secret, F19.3) — same
// exclusion shape as SpectatorOptions above. The 2s timeout here is IcecastListenerStatsSource's
// own resilience budget (SpectatorController.GetNowPlaying awaits this on every uncached request).
builder.Services.Configure<IcecastOptions>(cfg.GetSection(IcecastOptions.SectionName));
builder.Services.AddHttpClient<IcecastListenerStatsSource>(client => client.Timeout = TimeSpan.FromSeconds(2));
builder.Services.AddSingleton<IListenerStatsSource>(sp => sp.GetRequiredService<IcecastListenerStatsSource>());

// Container-level stack view for the admin Health page (gh-#148): DockerStats:BaseUrl points at
// the allowlisted docker-socket-proxy sidecar (compose service `dockerproxy` on the `stats`
// network). Env/compose-only, deliberately absent from StationSettingsAllowlist — deployment
// topology, same exclusion shape as IcecastOptions above. The 5s timeout bounds each proxy call:
// a one-shot /stats read blocks ~1s by design (the daemon takes the two cpu samples the
// percentage needs), so 5s is generous without letting a wedged sidecar pin a Health-page poll.
// DockerContainerStatsSource degrades to a well-formed `degraded: true` report on any failure —
// GET /api/health/containers never 500s over a missing sidecar.
builder.Services.Configure<DockerStatsOptions>(cfg.GetSection(DockerStatsOptions.SectionName));
builder.Services.AddHttpClient<DockerContainerStatsSource>(client => client.Timeout = TimeSpan.FromSeconds(5));

// gh-#10 (plugin-readiness P1.4): the listener-count time series — one sample every
// ListenerStats:PollSeconds published through the station event sink for a future analytics
// consumer. Rides the SAME IListenerStatsSource the spectator surface reads live.
builder.Services.Configure<ListenerStatsOptions>(cfg.GetSection(ListenerStatsOptions.SectionName));
builder.Services.AddSingleton<ListenerStatsSampler>();
builder.Services.AddHostedService<ListenerStatsPollerService>();

// Per-track artwork extraction cache (SPEC F88.3, STORY-222, PLAN T84): env/compose-only, same
// deployment-topology class as Tts:CacheRoot — ValidateOnStart mirrors every other options class
// bound this way (see ArtworkOptions' own remarks for why CacheDir defaults under the tts volume).
builder.Services
    .AddOptions<ArtworkOptions>()
    .Bind(cfg.GetSection(ArtworkOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<ArtworkService>();

// The SPEC F128.6 upload-normalize pipeline (STORY-333, STORY-339, PLAN T291): bounded read →
// magic-bytes gate → header-dims/APNG gate → ffmpeg center-crop-and-scale to a fresh 512×512 PNG.
// Consumed by AvatarPackController (T293) and PersonaAvatarController (T295); StationImageController
// (T307) is the next consumer. IImageProcessRunner is production-wired to FfmpegImageProcessRunner
// here (internal, InternalsVisibleTo GenWave.Host.Tests); tests substitute a counting fake directly
// against ImageNormalizeService's own constructor instead — no DI-container involvement needed
// for that seam.
builder.Services.AddSingleton<IImageProcessRunner, FfmpegImageProcessRunner>();
builder.Services.AddSingleton<ImageNormalizeService>();

// Respell→IPA assist for the pronunciation rules editor (SPEC F126.2, STORY-324, PLAN T278): the
// espeak-ng Process.Start adapter behind PronunciationDerivationController — a singleton so its
// IsAvailable latch (absent-until-restart, see EspeakRespellOracle's own remarks) is shared across
// every request rather than re-discovered per call. Registered directly here, not inside any
// AddGenWave* extension: this is an endpoint-only Host adapter (IRespellOracle lives in
// GenWave.Host.Pronunciations, not Abstractions/Core), deliberately never wired into
// AddGenWaveOrchestration/AddGenWavePlayout's own render-path graph above — see
// Story324_RespellOracle's DI-closure fact for the structural pin.
builder.Services.AddSingleton<IRespellOracle, EspeakRespellOracle>();

// Listener-request throttle knobs (SPEC F87, STORY-224, PLAN T86): env/compose-only, deliberately
// absent from StationSettingsAllowlist — an operator-tuned deployment setting, not a live PUT (the
// three keys that ARE live editable live on StationOptions.Requests, Station:Requests:* instead).
// ValidateOnStart mirrors ArtworkOptions just above.
builder.Services
    .AddOptions<RequestsOptions>()
    .Bind(cfg.GetSection(RequestsOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// The House Voice's endpoint caps (SPEC F143.4, STORY-357, PLAN T339): env/compose-only, deliberately
// absent from StationSettingsAllowlist for now — see AnnouncementsOptions' own remarks. ValidateOnStart
// mirrors RequestsOptions immediately above.
builder.Services
    .AddOptions<AnnouncementsOptions>()
    .Bind(cfg.GetSection(AnnouncementsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// The accepted-rate cap itself (SPEC F143.4, T339 review finding F1): a singleton so its
// FixedWindowRateLimiter's own window is shared by every request, wired to AnnouncementsController.Post
// directly rather than through the rate-limiter middleware — see AnnouncementAcceptedRateLimiter's own
// remarks for why. Registered after the ValidateOnStart above so a malformed AcceptedPerMinute fails
// boot before this ever resolves it.
builder.Services.AddSingleton<AnnouncementAcceptedRateLimiter>();

// Community-sourced content — currently just the Persona Catalog origin (SPEC F90.1, STORY-234,
// PLAN T99). Live via IOptionsMonitor<CommunityOptions> (read by CommunityCatalogAccessor, T101's
// eventual catalog endpoint consumer), so a PUT to Community:CatalogIndexUrl reaches the very next
// catalog request with no api restart. ValidateOnStart mirrors ArtworkOptions/RequestsOptions
// above; empty is a legal bound value (the F90.1 fail-closed kill switch), so no [Required].
builder.Services
    .AddOptions<CommunityOptions>()
    .Bind(cfg.GetSection(CommunityOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<CommunityCatalogAccessor>();

// Persona Catalog proxy (SPEC F90.2-F90.4, STORY-234, PLAN T100): the named client
// CatalogProxyService resolves via IHttpClientFactory per call (see its own remarks on
// HttpClientName for why this is a named client + plain AddSingleton, not a typed-client
// transient). AllowAutoRedirect is disabled on the primary handler — a redirect response is a
// fetch failure, never a hop this process takes (the SSRF ruling in CatalogProxyService's own
// remarks). MaxResponseContentBufferSize is INERT under CatalogHttpFetcher's
// HttpCompletionOption.ResponseHeadersRead (that option means SendAsync never auto-buffers, so
// this setting is never consulted) — kept anyway, sized to the largest F90.3 cap, purely as a
// regression backstop: if the completion option ever regresses back to buffering, the client
// throws instead of buffering an unbounded body. CatalogHttpFetcher's own bounded streaming read
// is what ACTUALLY enforces each per-file cap today.
builder.Services
    .AddHttpClient(CatalogProxyService.HttpClientName, client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);
        client.MaxResponseContentBufferSize = CatalogProxyService.MaxIndexBytes;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<CatalogProxyService>();

// Installs a catalog persona entry's own sidecar face after a successful catalog-origin import
// (SPEC F128.7, STORY-334, PLAN T297) — PersonaController.Import's own post-commit call site.
// Singleton for the same reason as CatalogProxyService/ImageNormalizeService just above: every
// dependency it composes is itself a singleton, and it carries no per-request state of its own.
// Registered against its own ICatalogPersonaAvatarInstaller seam (that interface's own remarks:
// PersonaController's existing direct-construction unit tests need a throwing stub, not this real
// dependency graph).
builder.Services.AddSingleton<ICatalogPersonaAvatarInstaller, CatalogPersonaAvatarInstaller>();

// The Library Gardener's file-action jail (SPEC F154.3, F154.5; STORY-379; PLAN T381, gh-#529):
// swap MediaLibrary's per-process-random-key HmacFileActionPlanTokens for a DataProtection-backed
// codec keyed the SAME way the admin cookie already is (AddGenWaveAdminApi's own
// AddDataProtection().PersistKeysToFileSystem(...), already registered above) — a plan token now
// survives an api container recreate, and IDataProtectionProvider is resolved lazily, so running
// AFTER .AddMediaLibrary(cfg) (which registers the HMAC default) is what makes this an override,
// not a race. Replace, not TryAdd/Add — exactly one IFileActionPlanTokens must ever be resolved.
builder.Services.Replace(
    ServiceDescriptor.Singleton<IFileActionPlanTokens, DataProtectionFileActionPlanTokens>());

// Boot validation for Library:Scan:QuarantineExemptRoots (SPEC F154.3; STORY-379; PLAN T381,
// gh-#529) — ScanOptions itself already binds inside .AddMediaLibrary(cfg) above via a plain
// Configure<T> call (never ValidateDataAnnotations()), so only the validator + its ValidateOnStart
// trigger are needed here; see ScanOptionsValidator's own remarks for why an un-rooted exempt root
// must fail boot rather than silently resolve against the process's own working directory.
builder.Services.AddSingleton<IValidateOptions<ScanOptions>, ScanOptionsValidator>();
builder.Services.AddOptions<ScanOptions>().ValidateOnStart();

builder.Services.AddControllers();

// Liveness endpoint for the compose healthcheck. No checks registered = 200 Healthy when up.
builder.Services.AddHealthChecks();

// Trust X-Forwarded-For AND X-Forwarded-Proto only from an operator-declared proxy network
// (Proxy:TrustedNetworks, env/compose-only — deferred finding from T04's review, STORY-171/T13;
// XForwardedProto added at T366 review MED-3). Empty by default: the middleware's own
// loopback-only KnownNetworks/KnownProxies defaults leave it inert behind a compose-network proxy
// (e.g. Caddy, PLAN T19's reference topology) until an operator opts in — never trust either
// header from an unlisted source (a spoofed IP would dodge the per-IP spectator limiter,
// RateLimiterPolicies.Spectator; a spoofed scheme would falsely mark a cookie Secure or, the other
// direction, falsely withhold it). Without XForwardedProto, Request.IsHttps (and therefore every
// CookieSecurePolicy.SameAsRequest / Request.IsHttps-conditioned cookie — the admin session cookie,
// AdminApiServiceCollectionExtensions, and the genwave-listener cookie, SpectatorThumbsController)
// never sees the edge's real scheme behind cloudflared -> Caddy: Kestrel itself only ever observes
// the plain-HTTP hop from Caddy, so Secure would never be stamped on the demo box at all.
var proxyOptions = cfg.GetSection(ProxyOptions.SectionName).Get<ProxyOptions>() ?? new ProxyOptions();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var cidr in proxyOptions.TrustedNetworks)
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));

    // gh-#129: the reference public topology is TWO trusted hops (cloudflared → Caddy). The
    // middleware's default ForwardLimit=1 stops the XFF walk on the inner proxy's container
    // address, so every public visitor rate-limits (and logs) as ONE caller — observed live as
    // cross-IP 429s on the request line. With any trusted network configured, let the walk run
    // until the first UNTRUSTED address (the real client): trust is enforced by KnownIPNetworks,
    // never by hop-count truncation. No trusted networks ⇒ default limit stays — the middleware
    // remains inert for direct-exposure deployments.
    if (proxyOptions.TrustedNetworks.Count > 0)
        options.ForwardLimit = null;
});

var app = builder.Build();

// Fail-closed admin gate (SPEC F60.4/STORY-164): loudly warn if the admin plane is locked down.
app.WarnIfAdminPasswordMissing();

// The plugin door's own boot narrative (SPEC F156.4/F156.7, PLAN T394): the ILogger WARN/INFO line
// and the booth-log row(s) PluginDoorServiceCollectionExtensions.AddGenWavePluginDoor's own
// PluginStatusAccessor recorded above — deferred to here because neither an ILogger nor an
// IBoothLogAppender exists until the host has finished building. No-op when the door never ran.
// ApplicationStopping (never CancellationToken.None), matching every other boot-time host token in
// this file — a shutdown mid-narration cancels the booth-log write cleanly instead of racing it.
await app.NarratePluginDoorAsync(app.Lifetime.ApplicationStopping);

// ── Middleware pipeline ──────────────────────────────────────────────────────
// Forwarded-headers processing runs first — anything downstream that reads Connection.RemoteIpAddress
// (the spectator/login rate limiters) must see the real client IP, not a fronting proxy's. Inert by
// default (see the ForwardedHeadersOptions configuration above).
app.UseForwardedHeaders();

// Stamp Cache-Control: no-store on all /api/* responses before auth/routing so
// even error responses (401, 403, 500) carry the header. See NoCacheApiMiddleware.
app.UseMiddleware<NoCacheApiMiddleware>();
app.UseRouting();

// Surface gate (SPEC F61, F62.2): decides whether a route EXISTS before identity is ever
// consulted. Must run after UseRouting (needs the matched endpoint's metadata) and before
// UseAuthentication (a disabled surface 404s instead of 401ing) — see SurfaceGateMiddleware.
app.UseMiddleware<SurfaceGateMiddleware>();

// Spectator security headers (gh-#180): CSP + framing/referrer/sniffing guards on every response
// whose endpoint carries SpectatorSurfaceAttribute — the public page, its assets, and
// /spectator/api/*. After the surface gate (a disabled surface's bare 404 must stay
// header-identical to an unmapped route, F61.2) and before the rate limiter (a spectator 429
// carries the same headers as a 200). The CSP's img-src/media-src pins are read live per request
// from Station:PublicBaseUrl/Station:PublicStreamUrl — see SpectatorSecurityHeadersMiddleware.
app.UseMiddleware<SpectatorSecurityHeadersMiddleware>();

// SPEC F61.5: rate limiting runs after the surface gate (a killed admin plane 404s before the
// limiter is ever consulted, STORY-166) and before authentication (an unauthenticated brute-force
// burst is throttled before it reaches identity checks).
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// OutputCache runs last in the pipeline, immediately before endpoint execution (the recommended
// placement — after routing/auth so a cached response is only ever served for a request that
// would otherwise have been allowed through). A cache hit still passed through the rate limiter
// above, so it still counts against a caller's budget (SPEC F62.3/F62.11) — simpler than teaching
// the limiter about cache hits, and correct: a caller flooding a cached route is still worth
// throttling. Only SpectatorController actions carry an [OutputCache] policy today — every other
// endpoint is unaffected.
app.UseOutputCache();

app.MapControllers();

// GET / — the public listener's landing route (SPEC F64.1). Redirects to /spectator, the
// spectator single-page app (PLAN T16, MapSpectatorPage below); marked SpectatorSurface so it is
// gated exactly like every other spectator route — by Station:SpectatorMode (SurfaceGateMiddleware's
// existing check) and, once Spectator:PublicPort is set, the public-listener isolation check — and
// reachable on the internal port too, same as the rest of the spectator surface.
app.MapGet("/", () => Results.Redirect("/spectator"))
    .WithMetadata(new SpectatorSurfaceAttribute())
    .RequireAuthorization(AuthorizationPolicies.Spectator);

// The spectator single-page app itself (SPEC F63.1–F63.5, STORY-173): hand-written HTML/CSS/JS
// served straight from wwwroot/spectator via endpoint routing, not UseStaticFiles — see
// SpectatorPageEndpoints for why static-file middleware would dodge both the surface gate and the
// public-listener isolation check.
app.MapSpectatorPage();

// The composed active-theme stylesheet the spectator page will link (SPEC F102.3, STORY-264,
// PLAN T160): SpectatorSurface-tagged like the page itself, so it is gated and public-port-safe
// the same way — see SpectatorThemeEndpoints' own remarks for why this needs no carve-out the way
// /fonts did, and for the caching contract (deliberately not fonts' long max-age).
app.MapSpectatorThemeEndpoint();

// The switcher's theme-list read (SPEC F102.10a, STORY-266, PLAN T166): same SpectatorSurface
// gating/caching contract as the stylesheet route above, plus the class-wide rate limit
// SpectatorController's actions carry (applied explicitly here since this is minimal-API, not a
// controller action) — see SpectatorThemesEndpoint's own remarks.
app.MapSpectatorThemesEndpoint();

// The admin surface's own copy of the composed active-theme stylesheet (SPEC F102.3, STORY-264,
// PLAN T161): AdminSurface-tagged (not Spectator) and anonymous — see AdminThemeEndpoints' own
// remarks for why the admin login page's pre-auth theming rules out gating this behind a cookie,
// and for why AdminSurface tagging alone (no Spectator tag) is what keeps it off the public port.
app.MapAdminThemeEndpoint();

// The canonical vendored-font route (SPEC F102, PLAN T173): shared by both surfaces, so it is
// unattributed by AdminSurface/SpectatorSurface (neither kill switch may strand the other's
// fonts) — see FontEndpoints and SurfaceGateMiddleware's own remarks for the matching public-port
// carve-out this needs instead.
app.MapFontEndpoints();

// Liveness probe — anonymous so the (conditional) deny-by-default policy never 401s it.
app.MapHealthChecks("/health").AllowAnonymous();

// Minimal-API media endpoints (F8): AllowAnonymous inside MapMediaEndpoints so the
// Liquidsoap/Orchestrator hot path stays reachable without a cookie.
app.MapMediaEndpoints();

// Internal server-to-server endpoints (core network only, AllowAnonymous — engine uses these
// at boot to pull its effective crossfade config from the settings overlay).  Not under /api/*
// so the NoCacheApiMiddleware and the Next.js rewrite do not touch them.
app.MapInternalEndpoints();

app.Run();
