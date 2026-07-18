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
/// Both flags are read live, per request, via <see cref="IOptionsMonitor{T}.CurrentValue"/> —
/// never captured at startup — so a container recreate with a new env value takes effect on the
/// very next request.
/// </summary>
public sealed class SurfaceGateMiddleware(
    RequestDelegate next,
    IOptionsMonitor<AdminOptions> adminOptions,
    IOptionsMonitor<StationOptions> stationOptions)
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

        await next(context);
    }
}
