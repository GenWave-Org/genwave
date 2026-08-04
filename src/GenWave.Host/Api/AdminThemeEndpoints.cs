using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using GenWave.Host.Options;
using GenWave.Host.Theming;

namespace GenWave.Host.Api;

/// <summary>
/// <c>GET /api/theme.css</c> — the admin surface's own copy of the composed active-theme
/// stylesheet (SPEC F102.3, STORY-264, PLAN T161). admin_ui reaches this through its existing
/// <c>next.config.ts</c> <c>/api/:path*</c> rewrite, so it is same-origin in the browser — no CORS,
/// and <c>style-src 'self'</c> holds there too. This is <see cref="SpectatorThemeEndpoints"/>'s
/// near-twin; see that type's remarks first for the shape both share. Only what genuinely differs
/// for the admin surface is re-argued here.
///
/// <para>
/// <b>Resolution seam.</b> Same shape as the spectator route — both now call the SAME
/// <see cref="ThemeCatalog.Resolve"/> (PLAN T164(c): each carried its own private copy of the
/// precedence cascade before this task unified it on <see cref="ThemeCatalog"/>, its natural home).
/// Full precedence: visitor cookie → <c>Station:Theme</c> settings row → env default → shipped
/// default (SPEC F102.5). Because the response now genuinely varies by the request's <c>Cookie</c>
/// header, this route emits <c>Vary: Cookie</c> too (PLAN T164 precondition (b)) — identical
/// reasoning to the spectator route's own remarks.
/// </para>
///
/// <para>
/// <b>Surface gate — admin, not spectator.</b> Tagged <see cref="AdminSurfaceAttribute"/>, the same
/// marker every other admin route carries: <see cref="SurfaceGateMiddleware"/> 404s this route when
/// <c>Admin:Enabled</c> is false, exactly like the admin login form itself
/// (<see cref="AuthController"/>) — a killed admin plane strands no admin asset, this one included.
/// It deliberately does NOT carry <see cref="SpectatorSurfaceAttribute"/>: that marker ties a route
/// to <c>Station:SpectatorMode</c> and the public-listener carve-out, neither of which describes
/// this surface. One consequence, structural rather than incidental:
/// <see cref="SurfaceGateMiddleware"/>'s public-listener isolation check only admits
/// <see cref="SpectatorSurfaceAttribute"/>-tagged endpoints (plus <c>/health</c> and <c>/fonts/*</c>)
/// on the public port — an unattributed route 404s there regardless of authorization, so this
/// route inherits the same public-port isolation every other admin route already has, with no
/// extra carve-out needed (pinned by <see cref="Story172_PublicListenerIsolation"/>).
/// </para>
///
/// <para>
/// <b>Authorization — anonymous, deliberately.</b> The admin login page renders BEFORE any session
/// exists (same precedent as <see cref="AuthController.Login"/>/<c>Logout</c>, both
/// <c>[AllowAnonymous]</c> on an otherwise cookie-gated controller). If this route demanded a
/// cookie, the login screen — themed same as everywhere else — would fetch its own stylesheet,
/// get a 401, and silently fall back to whatever the browser does with a failed stylesheet load:
/// no console-visible error most operators would ever notice, just an unstyled or
/// default-Wireless-flavoured login form (the exact "degrades silently" failure mode F102.7 exists
/// to avoid for the spectator surface — there is no reason the admin login should be worse off).
/// A cookie-authenticated page's own fetch would succeed regardless (browsers attach cookies to
/// same-origin sub-resource requests), so gating this would only ever break the one moment gating
/// it can't help: pre-auth. Weighed against what an anonymous caller actually learns: a colour
/// palette and font-face declarations for a theme whose manifest ships inside the public Docker
/// image — the same shipped resource anyone can already read straight off the image, and the exact
/// non-secret class <see cref="FontEndpoints"/> already serves anonymously for the same reason (see
/// that type's own remarks). There is nothing here an authenticated session would protect.
/// </para>
///
/// <para>
/// <b>This premise expires.</b> "Nothing here an authenticated session would protect" is true only
/// because, today, every theme reachable by <see cref="ThemeCatalog.Resolve"/> is first-party and
/// ships inside the public Docker image — anonymity is sound because the body is already public by
/// another route (byte-identical to <see cref="SpectatorThemeEndpoints"/>'s own response; verified
/// by <c>cmp</c> against both live routes at T161 review, not merely asserted). This holds just as
/// well now that resolution reads a request cookie (T164): the cookie only ever SELECTS among
/// already-public shipped themes, it never reveals anything about the visitor beyond that choice.
/// <b>Trigger:</b> the moment gh-#206 (Layer B) lets a theme be private or owner-only — a
/// catalog-fetched theme with restricted visibility, or an owner row in <c>station.theme</c> not
/// meant for anonymous eyes — this route stops being "the same shipped resource anyone can already
/// read" and starts being a disclosure vector: an anonymous caller reading a theme's tokens through
/// this endpoint that they could not read any other way. Whoever adds that capability to
/// <see cref="ThemeCatalog"/> must re-examine <c>.AllowAnonymous()</c> on this route (and on
/// <see cref="SpectatorThemeEndpoints"/>'s) before shipping it — this paragraph is that tripwire.
/// </para>
///
/// <para>
/// <b>Caching — identical contract to the spectator route.</b> <c>Station:Theme</c> is a live
/// setting (SPEC F102.14): a <c>PUT</c> must reach the very next request with no api restart, which
/// rules out a long <c>max-age</c> the way it does for <see cref="SpectatorThemeEndpoints"/>.
/// <c>Cache-Control: no-cache</c> plus a content-hash <c>ETag</c> forces revalidation on every
/// cache while still avoiding a re-sent body for an unchanged sheet, via 304 — and, now that content
/// genuinely varies per visitor (T164), <c>Vary: Cookie</c> tells any cache in front of this route
/// it must key on the request's <c>Cookie</c> header rather than reuse one entry for everyone.
/// Deliberately the SAME contract as the spectator route on every axis.
/// </para>
/// </summary>
static class AdminThemeEndpoints
{
    const string CssContentType = "text/css; charset=utf-8";

    // Matches SpectatorThemeEndpoints/SpectatorPageEndpoints/FontEndpoints: HEAD rides every route
    // GET is mapped on (RFC 9110 §9.3.2), through the same surface gate and authorization.
    static readonly string[] GetAndHead = ["GET", "HEAD"];

    public static void MapAdminThemeEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/theme.css", GetAndHead, ServeThemeCss)
            .WithMetadata(new AdminSurfaceAttribute())
            .AllowAnonymous();
    }

    static IResult ServeThemeCss(HttpContext context, ThemeCatalog catalog, IOptionsMonitor<StationOptions> stationOptions)
    {
        var theme = catalog.Resolve(
            cookieSlug: context.Request.Cookies[ThemeCatalog.CookieName],
            stationSlug: stationOptions.CurrentValue.Theme);
        var css = ThemeCssComposer.Compose(theme);
        var etag = new EntityTagHeaderValue($"\"{ComputeContentHash(css)}\"");

        // The response body now depends on the request's Cookie header (T164 precondition (b)) — a
        // shared cache must revalidate per visitor, not serve one visitor's theme to another.
        context.Response.Headers[HeaderNames.Vary] = "Cookie";

        // Stamped on BOTH the 200 and the 304 path below (RFC 7232 §4.1: a 304 must repeat the
        // validators a client would otherwise use to keep trusting its cached copy).
        context.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { NoCache = true };
        context.Response.GetTypedHeaders().ETag = etag;

        var ifNoneMatch = context.Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch is not null && ifNoneMatch.Any(candidate =>
                candidate.Equals(EntityTagHeaderValue.Any) || candidate.Compare(etag, useStrongComparison: true)))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Text(css, CssContentType);
    }

    static string ComputeContentHash(string css) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(css)));
}
