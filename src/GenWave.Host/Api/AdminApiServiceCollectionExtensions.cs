using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using GenWave.Host.Auth;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// The admin surface's cross-cutting wiring: admin options, Data Protection key persistence,
/// cookie authentication, the announce-token scheme (SPEC F145.3/.4, STORY-360, PLAN T340), and the
/// named authorization policies (SPEC F60, see <see cref="AuthorizationPolicies"/> for the policy
/// definitions themselves).
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
            })
            // The House Voice's Bearer scheme (SPEC F145.3/.4, STORY-360, PLAN T340) — registered as
            // an AVAILABLE scheme, deliberately NOT the default (AddAuthentication("Cookie") above
            // stays the default/challenge scheme unchanged). Only a route that explicitly lists
            // AnnounceTokenAuthenticationDefaults.SchemeName among its own
            // [Authorize(AuthenticationSchemes = ...)] ever has AnnounceTokenAuthenticationHandler
            // consulted — see that class's and AnnounceTokenAuthenticationDefaults' own remarks for
            // the full scope-fence design.
            .AddScheme<AuthenticationSchemeOptions, AnnounceTokenAuthenticationHandler>(
                AnnounceTokenAuthenticationDefaults.SchemeName, _ => { });

        // The announce-token hash store (SPEC F145.3/.4, STORY-360, PLAN T340) — same
        // ConnectionStrings:Station connection string every station-schema store in
        // StationSettingsHostingExtensions uses, reached through its own narrow seam
        // (IAnnounceTokenStore) rather than IStationSettingsStore so its two keys can never be
        // allowlisted by accident — see AnnounceTokenStore's own remarks (the SafeLoopSeedMarkerStore
        // precedent, F27.10). Registered here (not StationSettingsHostingExtensions) because this is
        // where the scheme that consumes it is wired, and because — unlike every store in that
        // extension — this one is never used to build the configuration overlay, so it has no
        // "before AddGenWaveStationSettings runs" boot-ordering constraint to honor.
        var stationConnStr = configuration.GetConnectionString("Station") ?? string.Empty;
        services.AddSingleton<IAnnounceTokenStore>(_ => new AnnounceTokenStore(stationConnStr));

        // Named policies (AdminOnly, Spectator) + the unconditional deny-ALL fallback — see
        // AuthorizationPolicies for the single registration point (SPEC F60).
        services.AddGenWaveAuthorizationPolicies();

        // Named rate-limiter policies (Login, Spectator, and the F87.3 Requests cooldown+daily-cap
        // limiter) — see RateLimiterPolicies for the single registration point (SPEC F61.5).
        services.AddGenWaveRateLimiting(configuration);

        return services;
    }
}
