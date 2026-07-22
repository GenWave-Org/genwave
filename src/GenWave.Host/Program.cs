using Microsoft.AspNetCore.HttpOverrides;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Health;
using GenWave.Host.Options;
using GenWave.Host.Playout;
using GenWave.Host.Seeding;
using GenWave.Host.Stats;
using GenWave.MediaLibrary;
using GenWave.Orchestration;
using GenWave.Tts;

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

// Process boot instant, captured once here — not lazily by DI on first resolution — so
// GET /api/status's startedAt (SPEC F28.6) reflects true process start.
builder.Services.AddSingleton(new ProcessStartTime(DateTimeOffset.UtcNow));

// Station settings overlay + store + persona store (ConnectionStrings:Station). Mutates
// builder.Configuration (appends the live overlay source), so it runs before anything binds options.
builder.AddGenWaveStationSettings();

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
    // Safe-loop authoring pipeline (F27): TTS render → jingle-bed mix → measure → authored insert.
    .AddGenWaveSafeSegmentAuthoring()
    // SEAM 1: the Orchestrator is the INextItemProvider (music + TTS patter interleave).
    .AddGenWaveOrchestration()
    // Real ranker-backed persona pick provider (SPEC F81.6 rung 0, F82; STORY-213, PLAN T64) —
    // MUST run after AddGenWaveOrchestration so its AddSingleton<IPersonaPickProvider> wins over
    // that call's own TryAddSingleton<..., NoOpPersonaPickProvider> default (see this extension's
    // own remarks).
    .AddGenWavePersonaRanking(cfg)
    // Playout chain: engine control → feeder → feeder service → PlayoutSupervisor (hosted).
    .AddGenWavePlayout()
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

builder.Services.AddControllers();

// Liveness endpoint for the compose healthcheck. No checks registered = 200 Healthy when up.
builder.Services.AddHealthChecks();

// Trust X-Forwarded-For only from an operator-declared proxy network (Proxy:TrustedNetworks,
// env/compose-only — deferred finding from T04's review, STORY-171/T13). Empty by default: the
// middleware's own loopback-only KnownNetworks/KnownProxies defaults leave it inert behind a
// compose-network proxy (e.g. Caddy, PLAN T19's reference topology) until an operator opts in —
// never trust the header from an unlisted source (a spoofed IP would dodge the per-IP spectator
// limiter, RateLimiterPolicies.Spectator).
var proxyOptions = cfg.GetSection(ProxyOptions.SectionName).Get<ProxyOptions>() ?? new ProxyOptions();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    foreach (var cidr in proxyOptions.TrustedNetworks)
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
});

var app = builder.Build();

// Fail-closed admin gate (SPEC F60.4/STORY-164): loudly warn if the admin plane is locked down.
app.WarnIfAdminPasswordMissing();

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
