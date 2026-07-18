using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace GenWave.Host.Api;

/// <summary>
/// The single registration point for every named authorization policy (SPEC F60) — the seam a
/// future role (e.g. a real RBAC role beyond the single shared admin password) extends by adding a
/// policy here, not by scattering <c>[Authorize]</c> shapes across controllers.
///
/// <list type="bullet">
///   <item><see cref="AdminOnly"/> gates the entire admin control plane. STORY-164/T02 made it
///   fail-closed (SPEC F60.4): <see cref="AdminOnlyAuthorizationHandler"/> only succeeds for an
///   authenticated cookie session, and no session can ever exist when <c>Admin:Password</c> is
///   empty because <see cref="AuthController.Login"/> always fails in that case — the historical
///   local-dev open mode is retired.</item>
///   <item><see cref="Spectator"/> is reserved for the public read-only surface (STORY-167+) —
///   registered now, demands nothing yet, and nothing references it yet.</item>
///   <item>The fallback policy is unconditional deny-ALL: any endpoint that is missing its explicit
///   policy/<c>[AllowAnonymous]</c> annotation is dead on arrival, even for an authenticated admin.</item>
/// </list>
/// </summary>
static class AuthorizationPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string Spectator = "Spectator";

    /// <summary>
    /// Registers the two named policies, the deny-ALL fallback, and the <see cref="AdminOnly"/>
    /// requirement's handler. Call once from <see cref="AdminApiServiceCollectionExtensions"/>.
    /// </summary>
    public static IServiceCollection AddGenWaveAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, AdminOnlyAuthorizationHandler>();

        // See FrameworkDiagnosticEndpointAuthorizationHandler: the one carve-out the deny-ALL
        // fallback needs so it never masks MVC's own 415/405 diagnostics behind a 401/403.
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, FrameworkDiagnosticEndpointAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminOnly, policy => policy.Requirements.Add(new AdminOnlyRequirement()));

            // Demands nothing yet (SPEC F60.2) — reserved for the public read-only surface.
            options.AddPolicy(Spectator, policy => policy.RequireAssertion(_ => true));

            // Deny-ALL: fails even for an authenticated principal (SPEC F60.3). An endpoint reaches
            // this only if it carries neither a named policy nor AllowAnonymous.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => false)
                .Build();
        });

        return services;
    }
}
