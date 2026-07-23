using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace GenWave.Host.Api;

/// <summary>
/// The single registration point for every named authorization policy (SPEC F60) — the seam a
/// future role (e.g. a real RBAC role beyond the single shared admin password) extends by adding a
/// policy here, not by scattering <c>[Authorize]</c> shapes across controllers.
///
/// <list type="bullet">
///   <item><see cref="AdminOnly"/> gates admin-plane session management (login/logout) — the one
///   concern every future role shares. STORY-164/T02 made it fail-closed (SPEC F60.4):
///   <see cref="AdminOnlyAuthorizationHandler"/> only succeeds for an authenticated cookie
///   session, and no session can ever exist when <c>Admin:Password</c> is empty because
///   <see cref="AuthController.Login"/> always fails in that case — the historical local-dev open
///   mode is retired.</item>
///   <item><see cref="Operator"/> / <see cref="Curation"/> / <see cref="Settings"/> /
///   <see cref="PlayoutRead"/> (gh-#8, plugin-readiness P1.2) are the granular admin-plane names an
///   RBAC module re-targets WITHOUT touching controllers: today all four carry the SAME
///   <see cref="AdminOnlyRequirement"/> — one shared admin password, deliberately no behavior
///   change — but each controller already declares WHICH plane it belongs to. Operator = keeping
///   the station on air (safe segments, TTS previews, voices); Curation = shaping the library and
///   its taste signals (media, libraries, ratings, re-enrichment, facets, taste thumbs);
///   Settings = station configuration (settings, corrections, personas); PlayoutRead = read-only
///   observation (live, status, booth log, LLM inspector).</item>
///   <item><see cref="Spectator"/> is reserved for the public read-only surface (STORY-167+) —
///   registered now, demands nothing yet.</item>
///   <item>The fallback policy is unconditional deny-ALL: any endpoint that is missing its explicit
///   policy/<c>[AllowAnonymous]</c> annotation is dead on arrival, even for an authenticated admin.</item>
/// </list>
/// </summary>
static class AuthorizationPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string Operator = "Operator";
    public const string Curation = "Curation";
    public const string Settings = "Settings";
    public const string PlayoutRead = "PlayoutRead";
    public const string Spectator = "Spectator";

    /// <summary>Every admin-plane policy name — the set an RBAC module differentiates (gh-#8).</summary>
    public static readonly IReadOnlyList<string> AdminPlanePolicies =
        [AdminOnly, Operator, Curation, Settings, PlayoutRead];

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
            // One registration per admin-plane name, all carrying the SAME requirement today
            // (gh-#8): the split is a seam, not a behavior change — an RBAC module differentiates
            // them by re-registering individual names here, never by touching controllers.
            foreach (var policyName in AdminPlanePolicies)
                options.AddPolicy(policyName, policy => policy.Requirements.Add(new AdminOnlyRequirement()));

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
