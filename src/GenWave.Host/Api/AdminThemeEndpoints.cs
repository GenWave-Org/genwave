using System.Security.Cryptography;
using System.Text;
using Microsoft.Net.Http.Headers;
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
/// <b>Resolution seam.</b> Same shape as the spectator route, deliberately duplicated rather than
/// shared: full precedence (visitor cookie → <c>Station:Theme</c> settings row → env default →
/// shipped default, SPEC F102.5) is T164's job and does not exist yet. This route reads no cookie
/// and no setting — it always serves the shipped default
/// (<see cref="ThemeCatalog.ShippedDefaultSlug"/>). <see cref="ResolveTheme"/> is the one line T164
/// replaces, on BOTH surfaces (PLAN T164(c): "Unify <c>ResolveTheme</c> across both surfaces" —
/// today each carries its own copy by necessity).
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
/// because, today, every theme reachable by <see cref="ResolveTheme"/> is first-party and ships
/// inside the public Docker image — anonymity is sound because the body is already public by
/// another route (byte-identical to <see cref="SpectatorThemeEndpoints"/>'s own response; verified
/// by <c>cmp</c> against both live routes at T161 review, not merely asserted). <b>Trigger:</b> the
/// moment gh-#206 (Layer B) lets a theme be private or owner-only — a catalog-fetched theme with
/// restricted visibility, or an owner row in <c>station.theme</c> not meant for anonymous eyes —
/// this route stops being "the same shipped resource anyone can already read" and starts being a
/// disclosure vector: an anonymous caller reading a theme's tokens through this endpoint that they
/// could not read any other way. Whoever adds that capability to <see cref="ThemeCatalog"/> or
/// <see cref="ResolveTheme"/> must re-examine <c>.AllowAnonymous()</c> on this route (and on
/// <see cref="SpectatorThemeEndpoints"/>'s) before shipping it — this paragraph is that tripwire.
/// </para>
///
/// <para>
/// <b>Caching — identical contract to the spectator route.</b> <c>Station:Theme</c> is a live
/// setting (SPEC F102.14): a <c>PUT</c> must reach the very next request with no api restart, which
/// rules out a long <c>max-age</c> the way it does for <see cref="SpectatorThemeEndpoints"/>.
/// <c>Cache-Control: no-cache</c> plus a content-hash <c>ETag</c> forces revalidation on every
/// cache while still avoiding a re-sent body for an unchanged sheet, via 304. Deliberately the SAME
/// contract as the spectator route so T164's eventual cookie/setting-driven content needs no
/// caching-shape change on either surface — only <see cref="ResolveTheme"/> does.
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

    /// <summary>T164's seam (SPEC F102.5/F102.6), same shape as
    /// <see cref="SpectatorThemeEndpoints"/>'s own copy. Today this always returns the shipped
    /// default — no cookie, no <c>Station:Theme</c> setting, no env default are read here; that
    /// whole precedence chain does not exist yet. T164 replaces this method's body (on both
    /// surfaces) with it; every other line in this file is unaffected by that change.</summary>
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
