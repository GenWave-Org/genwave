using System.Security.Cryptography;
using System.Text;
using Microsoft.Net.Http.Headers;
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
/// <c>Station:Theme</c> env default → shipped default (SPEC F102.5) — is T164's job; it does not
/// exist yet. This route deliberately reads no cookie and no setting: it always serves the shipped
/// default (<see cref="ThemeCatalog.ShippedDefaultSlug"/>, ARCHITECTURE "Theme system": "shipped
/// default <c>cats-whisker</c>"). <see cref="ResolveTheme"/> is the one line T164 replaces; nothing else
/// here should need to change when it does.
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
/// avoids re-sending the body, via 304. This is deliberately the SAME contract T164's
/// cookie/setting-driven content will need — <see cref="ResolveTheme"/> is the only thing that
/// changes; the revalidation shape below already does not assume the response is constant.
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

    static IResult ServeThemeCss(HttpContext context, ThemeCatalog catalog)
    {
        var theme = ResolveTheme(catalog);
        var css = ThemeCssComposer.Compose(theme);
        var etag = new EntityTagHeaderValue($"\"{ComputeContentHash(css)}\"");

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

    /// <summary>T164's seam (SPEC F102.5/F102.6). Today this always returns the shipped default —
    /// no cookie, no <c>Station:Theme</c> setting, no env default are read here; that whole
    /// precedence chain does not exist yet. T164 replaces this method's body with it; every other
    /// line in this file is unaffected by that change.</summary>
    static ThemeManifest ResolveTheme(ThemeCatalog catalog) =>
        catalog.TryGetBySlug(ThemeCatalog.ShippedDefaultSlug, out var theme)
            ? theme
            : throw new InvalidOperationException(
                $"shipped theme catalog is missing its own default slug '{ThemeCatalog.ShippedDefaultSlug}' — " +
                "this is a boot-time authoring bug (see Program.cs's own startup assertion, which " +
                "should have stopped the process before any request could reach here)");

    static string ComputeContentHash(string css) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(css)));
}
