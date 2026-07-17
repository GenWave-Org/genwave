using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Options;
using GenWave.Host.Playout;
using GenWave.Host.Seeding;
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
    // Playout chain: engine control → feeder → feeder service → PlayoutSupervisor (hosted).
    .AddGenWavePlayout()
    // Boot seed: branded safe-loop backstop (F27.6), one-shot + idempotent.
    .AddGenWaveSafeLoopSeed(cfg)
    // Admin surface: admin options, Data Protection, cookie auth, deny-by-default policy.
    .AddGenWaveAdminApi(cfg);

builder.Services.AddControllers();

// Liveness endpoint for the compose healthcheck. No checks registered = 200 Healthy when up.
builder.Services.AddHealthChecks();

var app = builder.Build();

// ── Middleware pipeline ──────────────────────────────────────────────────────
// Stamp Cache-Control: no-store on all /api/* responses before auth/routing so
// even error responses (401, 403, 500) carry the header. See NoCacheApiMiddleware.
app.UseMiddleware<NoCacheApiMiddleware>();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

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
