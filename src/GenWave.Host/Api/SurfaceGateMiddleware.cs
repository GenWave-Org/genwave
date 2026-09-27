using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
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
    IOptionsMonitor<SpectatorOptions> spectatorOptions,
    EndpointDataSource endpoints)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint is RouteEndpoint && IsGatedOff(endpoint.Metadata))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // gh-#635: routing's synthesized rejections (405 method-not-allowed, 415) are not
        // RouteEndpoints and carry none of the matched routes' metadata, so the checks above never
        // saw them — a 405 + Allow header leaked that a disabled surface's path exists. Judge the
        // rejection by the routes whose pattern matches the path: all gated off → 404, like the
        // routes themselves.
        if (endpoint is not null and not RouteEndpoint && AllCandidatesGatedOff(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Public listener isolation (SPEC F64.1/F64.2, STORY-172): when the operator has bound a
        // dedicated public port (Spectator:PublicPort > 0) and THIS request arrived on it, only
        // the spectator surface, /health, and /fonts/* may respond — admin, /media/*, /internal/*
        // 404 here, regardless of Admin:Enabled/Station:SpectatorMode, so a fronting-proxy misroute
        // onto the public port is structurally harmless. A request on any OTHER local port (the
        // internal port, or no public port configured at all) is entirely unaffected by this check.
        //
        // /fonts/* (PLAN T173) is path-matched here rather than SpectatorSurfaceAttribute-tagged,
        // deliberately: the spectator page fetches its own fonts same-origin, so this carve-out is
        // what lets those requests land when they arrive on the public port — but the vendored
        // faces are shared with admin too, and admin must keep reading them over the INTERNAL port
        // regardless of Station:SpectatorMode (default false), which the attribute-based checks
        // above gate on. Tagging the route SpectatorSurface instead would satisfy this carve-out
        // but reopen that: admin's own font requests never cross the public port at all (they ride
        // BACKEND_URL, the internal port), so a path check here costs nothing on that side while
        // keeping the two concerns — "does this surface exist" vs. "is this port public-safe" —
        // independent, the same separation /health already models.
        var publicPort = spectatorOptions.CurrentValue.PublicPort;
        if (publicPort > 0 && context.Connection.LocalPort == publicPort)
        {
            var isHealthCheck = context.Request.Path.StartsWithSegments(
                "/health", StringComparison.OrdinalIgnoreCase);
            var isFontAsset = context.Request.Path.StartsWithSegments(
                "/fonts", StringComparison.OrdinalIgnoreCase);
            var isSpectatorSurface = endpoint?.Metadata.GetMetadata<SpectatorSurfaceAttribute>() is not null;

            if (!isHealthCheck && !isFontAsset && !isSpectatorSurface)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }

        await next(context);
    }

    /// <summary>True when any surface tag on <paramref name="metadata"/> names a surface that is
    /// switched off right now.</summary>
    bool IsGatedOff(EndpointMetadataCollection metadata)
    {
        if (metadata.GetMetadata<AdminSurfaceAttribute>() is not null && !adminOptions.CurrentValue.Enabled)
            return true;

        if (metadata.GetMetadata<SpectatorSurfaceAttribute>() is not null && !stationOptions.CurrentValue.SpectatorMode)
            return true;

        // Listener-request kill switch (SPEC F87.2, STORY-224, PLAN T87): independent of
        // Station:SpectatorMode above — see RequestsSurfaceAttribute's own remarks for why this
        // runs here (before the rate limiter) rather than as an in-action check.
        if (metadata.GetMetadata<RequestsSurfaceAttribute>() is not null && !stationOptions.CurrentValue.Requests.Enabled)
            return true;

        // Taste-thumb kill switch (SPEC F150.2, STORY-369, PLAN T366): the same independent,
        // before-the-limiter shape as RequestsSurfaceAttribute immediately above — see
        // ThumbsSurfaceAttribute's own remarks.
        return metadata.GetMetadata<ThumbsSurfaceAttribute>() is not null && !stationOptions.CurrentValue.Thumbs.Enabled;
    }

    /// <summary>
    /// True when at least one mapped route's pattern matches <paramref name="path"/> and every such
    /// route is gated off. Rejection path only (a 405/415 is rare), so the matchers are built per call.
    /// Inline constraints are ignored — a looser match can only add candidates, and one open
    /// candidate keeps the framework's own answer.
    /// </summary>
    bool AllCandidatesGatedOff(PathString path)
    {
        var any = false;
        foreach (var route in endpoints.Endpoints.OfType<RouteEndpoint>())
        {
            var matcher = new TemplateMatcher(new RouteTemplate(route.RoutePattern), new RouteValueDictionary());
            if (!matcher.TryMatch(path, new RouteValueDictionary()))
                continue;

            if (!IsGatedOff(route.Metadata))
                return false;
            any = true;
        }
        return any;
    }
}
