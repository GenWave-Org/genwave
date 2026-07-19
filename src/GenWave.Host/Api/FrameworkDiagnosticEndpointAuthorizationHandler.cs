using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Routing;

namespace GenWave.Host.Api;

/// <summary>
/// One carve-out on top of the default authorization result handling: MVC's own protocol-negotiation
/// diagnostic responses (e.g. the built-in "415 Unsupported Media Type" / "405 Method Not Allowed"
/// endpoints synthesized during action selection when no candidate accepts the request) carry no
/// metadata at all and are never a <see cref="RouteEndpoint"/> — every endpoint THIS application maps
/// (controllers, minimal APIs, health checks) is always a <see cref="RouteEndpoint"/>. Without this
/// carve-out, the unconditional deny-ALL <c>FallbackPolicy</c> (SPEC F60.3) intercepts those framework
/// diagnostics — which have neither a named policy nor <c>[AllowAnonymous]</c> — and masks a
/// meaningful 415/405 behind a 401/403 for anonymous callers, which is not the fail-closed intent of
/// F60.3 (that applies to routes THIS codebase forgot to annotate, not to the framework's own
/// content-negotiation responses). Real, unannotated application endpoints are completely unaffected —
/// they are always <see cref="RouteEndpoint"/>s, so the deny-ALL fallback still governs them exactly
/// as before.
/// </summary>
sealed class FrameworkDiagnosticEndpointAuthorizationHandler : IAuthorizationMiddlewareResultHandler
{
    static readonly AuthorizationMiddlewareResultHandler Default = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        // Two pass-through cases when authorization fails:
        //   null endpoint      — no route matched; nothing would run. The deny-ALL fallback still
        //                        evaluates for endpoint-less requests (AuthorizationMiddleware
        //                        combines the fallback over empty metadata), and masking the
        //                        terminal 404 as a 401 would violate the "surface does not exist"
        //                        semantics (SPEC F61.2/F62.2).
        //   non-RouteEndpoint  — the framework's synthesized 405/415 diagnostics described above.
        // Every real application endpoint is a RouteEndpoint and stays fully governed by the
        // deny-ALL fallback.
        if (!authorizeResult.Succeeded && context.GetEndpoint() is not RouteEndpoint)
            return next(context);

        return Default.HandleAsync(next, context, policy, authorizeResult);
    }
}
