using Microsoft.Net.Http.Headers;

namespace GenWave.Host.Api;

/// <summary>
/// <c>GET /fonts/{file}</c> — the one canonical, api-served home for the vendored <c>.woff2</c>
/// faces (SPEC F102, PLAN T173, ARCHITECTURE.md "Theme system" decision table). Both surfaces'
/// <c>@font-face</c> declarations point here: spectator's <c>styles.css</c> directly (same
/// origin), admin-ui's <c>globals.css</c> through its existing <c>next.config.ts</c> rewrite
/// (<c>/fonts/:path*</c> → <c>BACKEND_URL</c>, same shape as its <c>/api/:path*</c> rewrite).
/// Before this route the three binaries lived in two places (<c>wwwroot/spectator/fonts</c> and
/// <c>admin-ui/app/fonts</c>, byte-identical) and admin's copy was <c>next/font/local</c>-loaded,
/// which content-hashes into <c>.next/static/media/*</c> — no stable URL a theme manifest (T156's
/// <c>FontSrcPattern</c>) could ever name. One route, one copy, lowercase-hyphenated filenames.
///
/// <para>
/// Deliberately unattributed by <see cref="SpectatorSurfaceAttribute"/>/<see cref="AdminSurfaceAttribute"/>
/// and <c>.AllowAnonymous()</c> rather than a named authorization policy: these are non-sensitive,
/// shared static assets both the (unauthenticated) admin login page and the public spectator page
/// need before any session exists. Neither the <c>Admin:Enabled</c> nor the
/// <c>Station:SpectatorMode</c> kill switch should be able to strand the OTHER surface without its
/// type system — <c>Station:SpectatorMode</c> defaults false (appsettings.json), so gating this on
/// <see cref="SpectatorSurfaceAttribute"/> would 404 admin's own fonts out of the box. See
/// <see cref="SurfaceGateMiddleware"/>'s own remarks for the matching, deliberately path-based
/// public-listener-isolation carve-out this route needs instead.
/// </para>
///
/// <para>
/// Every route matches exactly one path segment and switches on a literal, known filename — the
/// request-supplied segment is only ever compared for equality, never concatenated into a
/// filesystem path (the same structural closure <see cref="SpectatorPageEndpoints"/> uses) — so
/// there is no path-traversal surface even though the files themselves are served straight off
/// disk.
/// </para>
/// </summary>
static class FontEndpoints
{
    /// <summary>A year, plus the <c>immutable</c> extension (matches
    /// <see cref="SpectatorArtworkController"/>'s own immutable-asset budget): the filename IS the
    /// asset's identity here — a changed face ships under a new name, never overwrites this one in
    /// place — so there is nothing for a client to ever revalidate.</summary>
    const int ImmutableMaxAgeSeconds = 31536000;

    const string FontContentType = "font/woff2";

    static readonly string[] GetAndHead = ["GET", "HEAD"];

    public static void MapFontEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/fonts/{file}", GetAndHead, ServeFont)
            .AllowAnonymous();
    }

    static IResult ServeFont(string file, HttpContext context, IWebHostEnvironment env) =>
        file switch
        {
            "fraunces-variable-latin.woff2" => ServeFile(context, env, "fraunces-variable-latin.woff2"),
            "fraunces-italic-variable-latin.woff2" => ServeFile(context, env, "fraunces-italic-variable-latin.woff2"),
            "source-sans-3-variable-latin.woff2" => ServeFile(context, env, "source-sans-3-variable-latin.woff2"),
            "jetbrains-mono-variable-latin.woff2" => ServeFile(context, env, "jetbrains-mono-variable-latin.woff2"),
            "grenze-gotisch-variable-latin.woff2" => ServeFile(context, env, "grenze-gotisch-variable-latin.woff2"),
            _ => Results.NotFound(),
        };

    /// <summary><paramref name="fileName"/> is always a literal chosen by the switch above — never
    /// the raw request segment — so this never opens a path-traversal surface regardless of what a
    /// caller puts in the URL.</summary>
    static IResult ServeFile(HttpContext context, IWebHostEnvironment env, string fileName)
    {
        var cacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromSeconds(ImmutableMaxAgeSeconds),
        };
        cacheControl.Extensions.Add(new NameValueHeaderValue("immutable"));
        context.Response.GetTypedHeaders().CacheControl = cacheControl;

        // Unlike SpectatorSecurityHeadersMiddleware's full set, only nosniff belongs here:
        // X-Frame-Options/Referrer-Policy/CSP are document-level policies a font binary has no use
        // for (see that middleware's remarks on why /fonts/* is excluded), but nosniff still governs
        // THIS response specifically — it tells the browser not to second-guess the explicit
        // font/woff2 content type above. A deliberate stamp, not an accidental omission.
        context.Response.Headers.XContentTypeOptions = "nosniff";

        var fullPath = Path.Combine(env.ContentRootPath, "wwwroot", "fonts", fileName);
        return Results.File(fullPath, FontContentType);
    }
}
