using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Host.Engine;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// Internal server-to-server endpoints reachable on the <c>core</c> network by the engine
/// and other sidecar services.  These are NOT under <c>/api/*</c> and therefore:
///   • the <see cref="NoCacheApiMiddleware"/> does not set no-store on them, which is fine
///     because engine requests are not browser-facing;
///   • the Next.js <c>/api/*</c> rewrite never proxies them to the browser;
///   • they are explicitly <see cref="AllowAnonymousAttribute"/> because the engine has no
///     session cookie — network isolation (the <c>core</c> Docker network) is the boundary.
///
/// Security rationale: this group exposes only operator-controlled tuning numbers (crossfade
/// and safe-gap seconds). No secrets, no connection strings, no per-user data. An attacker who
/// can reach this endpoint is already on the internal Docker network and can already reach the
/// unauthenticated Liquidsoap telnet port on :1234, which is a far higher-value target.
/// Adding authentication here would give no real defence.
/// </summary>
static class InternalEndpoints
{
    /// <summary>
    /// Keys emitted by the engine-config endpoint — exactly these three, nothing else.
    /// GW_SAFE_GAP_SECONDS rides the same path as GW_XFADE_MIN/MAX (F29.8, STORY-100): it must
    /// appear here or a PUT /api/settings override would persist to the overlay but never reach
    /// the engine on its next boot.
    /// </summary>
    static readonly string[] EngineConfigKeys = ["GW_XFADE_MIN", "GW_XFADE_MAX", "GW_SAFE_GAP_SECONDS"];

    public static RouteGroupBuilder MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        // Group under /internal; AllowAnonymous so the deny-by-default fallback policy
        // (set when Admin:Password is non-empty) does not 401 the engine's boot-time fetch.
        var group = app.MapGroup("/internal").AllowAnonymous();

        // GET /internal/engine-config
        // Returns a shell-sourceable text/plain body:
        //   GW_XFADE_MIN=<effective-value>
        //   GW_XFADE_MAX=<effective-value>
        //   GW_SAFE_GAP_SECONDS=<effective-value>
        //
        // "Effective value" = overlay wins over appsettings default (IConfiguration already
        // merges the station.settings provider after env/appsettings in Program.cs, so a
        // stored override is automatically visible here).
        //
        // Only the keys in EngineConfigKeys are ever emitted — never any other config key.
        group.MapGet("/engine-config", (IConfiguration configuration) =>
        {
            var lines = EngineConfigKeys
                .Select(key =>
                {
                    var value = configuration[key] ?? string.Empty;
                    return $"{key}={value}";
                });

            var body = string.Join('\n', lines) + '\n';

            return Results.Text(body, contentType: "text/plain");
        });

        // GET /internal/safe-track
        // Called by the engine's request.dynamic on each queue drain (SPEC F21.2). Returns a
        // single Liquidsoap annotate:...:/path line drawn from the configured SafeScope libraries,
        // or 204 No Content when the scope is empty / no ready tracks exist (SPEC F21.5).
        // AllowAnonymous is inherited from the group — the engine has no session cookie and
        // network isolation (the core Docker network) is the security boundary (SPEC F21.2).
        // Cache-Control: no-store is stamped so a live SafeScope edit (K4) takes effect on the
        // very next engine call without requiring a restart (SPEC F21.6).
        group.MapGet("/safe-track", (
            IMediaCatalog catalog,
            IOptionsMonitor<StationOptions> stationMonitor,
            IOptionsMonitor<LoudnessOptions> loudnessMonitor,
            ILoggerFactory loggerFactory,
            HttpResponse response,
            CancellationToken ct) =>
            HandleSafeTrackAsync(catalog, stationMonitor, loudnessMonitor,
                loggerFactory.CreateLogger(typeof(InternalEndpoints)), response, ct));

        return group;
    }

    /// <summary>
    /// Handler for GET /internal/safe-track. Reads <see cref="StationOptions.SafeScope"/> via
    /// <see cref="IOptionsMonitor{T}.CurrentValue"/> on <b>every call</b> so a live edit applied
    /// by K4 takes effect on the next engine request without an API restart.
    /// </summary>
    /// <remarks>
    /// Exposed as <c>internal static</c> so the unit-test suite can invoke it directly with
    /// fake dependencies (avoids spinning up a full <see cref="WebApplication"/> for each AC).
    /// </remarks>
    internal static async Task<IResult> HandleSafeTrackAsync(
        IMediaCatalog catalog,
        IOptionsMonitor<StationOptions> stationMonitor,
        IOptionsMonitor<LoudnessOptions> loudnessMonitor,
        ILogger logger,
        HttpResponse response,
        CancellationToken ct)
    {
        var station = stationMonitor.CurrentValue;
        var loudness = loudnessMonitor.CurrentValue;
        var scope = new LibraryScope(station.SafeScope.LibraryIds.ToArray());

        // Stamp no-store on every response (200 and 204) so the engine never caches
        // a "no available track" 204 beyond the current request cycle.
        response.Headers.CacheControl = "no-store";

        if (scope.IsEmpty)
        {
            logger.LogWarning("safe-track 204: SafeScope empty (F4.4 degraded mode)");
            return Results.NoContent();
        }

        // No recent-exclusion per SPEC F21.11: the safe library is intentionally small,
        // so repeat-avoidance is deliberately disabled here.
        var reference = await catalog.GetRandomReadyAsync(scope, Array.Empty<string>(), ct);
        if (reference is null)
        {
            logger.LogWarning("safe-track 204: SafeScope has libraries but no ready+measurable+eligible rows");
            return Results.NoContent();
        }

        var item = reference.ToMediaItem();

        var gainDb = Gain.NormGainDb(reference.Loudness, loudness.TargetLufs, loudness.CeilingDbtp);
        var annotation = LiquidsoapAnnotationBuilder.Build(item, gainDb, station.Id, station.Name);

        return Results.Text(annotation, contentType: "text/plain");
    }
}
