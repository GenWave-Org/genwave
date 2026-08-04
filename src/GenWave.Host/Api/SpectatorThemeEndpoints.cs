using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using GenWave.Host.Options;
using GenWave.Host.Theming;

namespace GenWave.Host.Api;

/// <summary>
/// <c>GET /spectator/theme.css</c> — the composed active-theme stylesheet the spectator page will
/// link (SPEC F102.3, STORY-264, PLAN T160). Serves <see cref="ThemeCssComposer"/>'s output
/// verbatim as <c>text/css</c>, never inlined — see <see cref="ThemeCssComposer"/>'s own remarks
/// for why that is a CSP consequence, not a style preference.
///
/// <para>
/// <b>Resolution seam.</b> Full precedence — visitor cookie → <c>Station:Theme</c> settings row →
/// <c>Station:Theme</c> env default → shipped default (SPEC F102.5) — is <see cref="ThemeCatalog.Resolve"/>,
/// unified across both theme endpoints at PLAN T164 (each carried its own copy before that). The
/// cookie is read straight off the request under <see cref="ThemeCatalog.CookieName"/>; the station
/// value comes from <see cref="StationOptions.Theme"/> via <c>IOptionsMonitor</c>, read fresh per
/// request so a live <c>PUT /api/settings</c> reaches the very next request with no api restart.
/// Because the response now genuinely varies by the request's <c>Cookie</c> header, this route emits
/// <c>Vary: Cookie</c> (PLAN T164 precondition (b)) — without it, a shared cache holding one entry
/// per URL would thrash between visitors carrying different theme cookies, and every request would
/// degrade to a full 200, defeating the <c>ETag</c> below entirely.
/// </para>
///
/// <para>
/// <b>Surface gate.</b> Tagged <see cref="SpectatorSurfaceAttribute"/>, the same pairing every
/// other spectator route carries (<see cref="SpectatorPageEndpoints"/>, <see cref="SpectatorController"/>):
/// <see cref="SurfaceGateMiddleware"/> 404s the whole route when <c>Station:SpectatorMode</c> is
/// off, and — for free, from the same attribute — the public-listener isolation check already
/// admits any <see cref="SpectatorSurfaceAttribute"/>-tagged endpoint on the public port. Unlike
/// <see cref="FontEndpoints"/>'s <c>/fonts/{file}</c>, this content is NOT shared with admin (T161
/// serves admin's own copy at <c>/api/theme.css</c>, reached through its own rewrite), so there is
/// no reason to reach for <see cref="SurfaceGateMiddleware"/>'s path-based carve-out the way fonts
/// had to — the normal attribute mechanism is sufficient and keeps that carve-out list from
/// growing without cause.
/// </para>
///
/// <para>
/// <b>Caching — a deliberate departure from <see cref="FontEndpoints"/>.</b> A vendored face's
/// filename IS its identity (a changed face ships under a new name), which is what earns
/// <c>max-age=31536000, immutable</c>. This sheet has no such invariant: <c>Station:Theme</c> is an
/// allowlisted LIVE setting (SPEC F102.14) — a <c>PUT</c> must reach the very next request with no
/// api restart. A long <c>max-age</c> would let a browser or CDN keep serving yesterday's theme for
/// up to a year after an operator changes it — invisible in dev (one client, no shared cache) and
/// silently wrong the moment a reverse proxy or CDN sits in front of it in production. Instead:
/// <c>Cache-Control: no-cache</c> plus a content-hash <c>ETag</c>. <c>no-cache</c> forces every
/// cache — browser or CDN — to revalidate with the origin before reusing a stored response, so
/// staleness can never outlive a single round trip; the <c>ETag</c> means an unchanged sheet still
/// avoids re-sending the body, via 304 — now paired with <c>Vary: Cookie</c> above, since T164 made
/// the body genuinely visitor-dependent and a cache must key on that, not just revalidate it.
/// </para>
/// </summary>
static class SpectatorThemeEndpoints
{
    const string CssContentType = "text/css; charset=utf-8";

    // gh-#160: HEAD rides every route GET is mapped on (RFC 9110 §9.3.2), through the same surface
    // gate and authorization — matches SpectatorPageEndpoints/FontEndpoints.
    static readonly string[] GetAndHead = ["GET", "HEAD"];

    public static void MapSpectatorThemeEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/spectator/theme.css", GetAndHead, ServeThemeCss)
            .WithMetadata(new SpectatorSurfaceAttribute())
            .RequireAuthorization(AuthorizationPolicies.Spectator);
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
