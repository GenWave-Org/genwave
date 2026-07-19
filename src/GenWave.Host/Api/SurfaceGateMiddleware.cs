using Microsoft.Extensions.Options;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// Decides whether an endpoint EXISTS for this request, before authentication/authorization ever
/// run (SPEC F61, F62.2). A disabled surface returns a bare 404 — the same shape as an unmapped
/// route (no body, just the status code) — so a misrouted request or fronting-proxy leak reveals
/// nothing, not even a login prompt (F61.2).
///
/// Runs after <c>UseRouting</c> (so <see cref="HttpContext.GetEndpoint"/> is populated) and before
/// <c>UseAuthentication</c> (so a disabled surface never reaches identity checks at all —
/// existence is decided first). See <c>Program.cs</c> for the exact pipeline position.
///
/// All three settings are read live, per request, via <see cref="IOptionsMonitor{T}.CurrentValue"/>
/// — never captured at startup — so a container recreate with a new env value takes effect on the
/// very next request.
/// </summary>
public sealed class SurfaceGateMiddleware(
    RequestDelegate next,
    IOptionsMonitor<AdminOptions> adminOptions,
    IOptionsMonitor<StationOptions> stationOptions,
    IOptionsMonitor<SpectatorOptions> spectatorOptions)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint?.Metadata.GetMetadata<AdminSurfaceAttribute>() is not null
            && !adminOptions.CurrentValue.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (endpoint?.Metadata.GetMetadata<SpectatorSurfaceAttribute>() is not null
            && !stationOptions.CurrentValue.SpectatorMode)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Public listener isolation (SPEC F64.1/F64.2, STORY-172): when the operator has bound a
        // dedicated public port (Spectator:PublicPort > 0) and THIS request arrived on it, only
        // the spectator surface and /health may respond — admin, /media/*, /internal/* 404 here,
        // regardless of Admin:Enabled/Station:SpectatorMode, so a fronting-proxy misroute onto the
        // public port is structurally harmless. A request on any OTHER local port (the internal
        // port, or no public port configured at all) is entirely unaffected by this check.
        var publicPort = spectatorOptions.CurrentValue.PublicPort;
        if (publicPort > 0 && context.Connection.LocalPort == publicPort)
        {
            var isHealthCheck = context.Request.Path.StartsWithSegments(
                "/health", StringComparison.OrdinalIgnoreCase);
            var isSpectatorSurface = endpoint?.Metadata.GetMetadata<SpectatorSurfaceAttribute>() is not null;

            if (!isHealthCheck && !isSpectatorSurface)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }

        await next(context);
    }
}
