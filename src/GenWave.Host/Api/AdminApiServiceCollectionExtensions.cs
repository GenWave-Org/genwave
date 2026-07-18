using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// The admin surface's cross-cutting wiring: admin options, Data Protection key persistence,
/// cookie authentication, and the named authorization policies (SPEC F60,
/// see <see cref="AuthorizationPolicies"/> for the policy definitions themselves).
/// </summary>
static class AdminApiServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveAdminApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.SectionName));

        // ── Data Protection (cookie signing) ────────────────────────────────
        // Keys persist to the dp_keys volume so the auth cookie survives api container recreation.
        var dpOptions = configuration.GetSection(KeyRingOptions.SectionName).Get<KeyRingOptions>()
            ?? new KeyRingOptions();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dpOptions.KeyRingPath))
            .SetApplicationName("GenWave");

        // ── Cookie authentication (single config password) ──────────────────
        var adminOpts = configuration.GetSection(AdminOptions.SectionName).Get<AdminOptions>() ?? new AdminOptions();

        services.AddAuthentication("Cookie")
            .AddCookie("Cookie", o =>
            {
                o.Cookie.Name = adminOpts.CookieName;
                o.Cookie.HttpOnly = true;
                // SameAsRequest (not Always) so the cookie also works over plain HTTP on localhost.
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.Path = "/";
                o.ExpireTimeSpan = TimeSpan.FromHours(adminOpts.SessionLifetimeHours);
                o.SlidingExpiration = false;

                // This is a JSON API — return 401/403 instead of redirecting to a login page.
                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = ctx =>
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    },
                };
            });

        // Named policies (AdminOnly, Spectator) + the unconditional deny-ALL fallback — see
        // AuthorizationPolicies for the single registration point (SPEC F60).
        services.AddGenWaveAuthorizationPolicies();

        return services;
    }
}
