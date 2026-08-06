using Microsoft.Net.Http.Headers;
using GenWave.Host.Theming;

namespace GenWave.Host.Api;

/// <summary>
/// <c>GET /fonts/{file}</c> — the one canonical, api-served home for every serveable <c>.woff2</c>
/// face, vendored OR installed (SPEC F102, F104.6/F104.8, PLAN T173/T200, ARCHITECTURE.md "Theme
/// system"/"Community Catalog v2 → wardrobe"). Both surfaces' <c>@font-face</c> declarations point
/// here: spectator's <c>styles.css</c> directly (same origin), admin-ui's <c>globals.css</c> through
/// its existing <c>next.config.ts</c> rewrite (<c>/fonts/:path*</c> → <c>BACKEND_URL</c>, same shape
/// as its <c>/api/:path*</c> rewrite). Before this route the three vendored binaries lived in two
/// places (<c>wwwroot/spectator/fonts</c> and <c>admin-ui/app/fonts</c>, byte-identical) and admin's
/// copy was <c>next/font/local</c>-loaded, which content-hashes into <c>.next/static/media/*</c> — no
/// stable URL a theme manifest (T156's <c>FontSrcPattern</c>) could ever name. One route, one copy,
/// lowercase-hyphenated filenames.
///
/// <para>
/// Deliberately unattributed by <see cref="SpectatorSurfaceAttribute"/>/<see cref="AdminSurfaceAttribute"/>
/// and <c>.AllowAnonymous()</c> rather than a named authorization policy: these are non-sensitive,
/// shared assets both the (unauthenticated) admin login page and the public spectator page
/// need before any session exists. Since T200 the anonymous set also includes INSTALLED pack faces —
/// public-catalog content the operator chose to install, whose file names surface only through admin
/// responses or a worn theme's own CSS; non-enumerability is the deliberate and only guard
/// (F104.15's worn-theme disclosure model, re-audited at T209). Neither the <c>Admin:Enabled</c> nor the
/// <c>Station:SpectatorMode</c> kill switch should be able to strand the OTHER surface without its
/// type system — <c>Station:SpectatorMode</c> defaults false (appsettings.json), so gating this on
/// <see cref="SpectatorSurfaceAttribute"/> would 404 admin's own fonts out of the box. See
/// <see cref="SurfaceGateMiddleware"/>'s own remarks for the matching, deliberately path-based
/// public-listener-isolation carve-out this route needs instead.
/// </para>
///
/// <para>
/// <b>The closed set widens (SPEC F104.6, PLAN T200) — still non-enumerable.</b> The five vendored
/// names below stay a literal switch (unchanged from T173: the request-supplied segment is only ever
/// compared for equality, never concatenated into a filesystem path — the same structural closure
/// <see cref="SpectatorPageEndpoints"/> uses — so there is no path-traversal surface even though those
/// files are served straight off disk). A miss on that switch now falls through to
/// <see cref="InstalledFontCatalog.TryGetFace"/> — a plain in-memory dictionary lookup by the SAME
/// raw segment, never a filesystem path or a per-request store query (see that class's own
/// remarks) — before finally 404ing. The union of "vendored literal" and "currently-loaded installed
/// file" is the whole set this route will ever serve; nothing enumerates either half anywhere (SPEC
/// F104.6 "non-enumerable" — see <c>Story264_AnonymousApiSurface</c>'s own route-table-sweep idiom,
/// reused by <c>Story283_InstalledFontServing</c>'s own no-listing-route pin).
/// </para>
/// </summary>
static class FontEndpoints
{
    /// <summary>A year, plus the <c>immutable</c> extension (matches
    /// <see cref="SpectatorArtworkController"/>'s own immutable-asset budget): the filename IS the
    /// asset's identity here — a changed face ships under a new name, never overwrites this one in
    /// place — so there is nothing for a client to ever revalidate. Applies identically to an
    /// installed face: <c>station.font_pack_face.file</c> is likewise immutable-by-identity (a
    /// re-install upserts the OWNING PACK's row, but PLAN T208's uninstall — not this route — is the
    /// only way a given file name ever stops resolving).</summary>
    const int ImmutableMaxAgeSeconds = 31536000;

    const string FontContentType = "font/woff2";

    static readonly string[] GetAndHead = ["GET", "HEAD"];

    public static void MapFontEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/fonts/{file}", GetAndHead, ServeFont)
            .AllowAnonymous();
    }

    static IResult ServeFont(string file, HttpContext context, IWebHostEnvironment env, InstalledFontCatalog installedFonts) =>
        file switch
        {
            "fraunces-variable-latin.woff2" => ServeVendoredFile(context, env, "fraunces-variable-latin.woff2"),
            "fraunces-italic-variable-latin.woff2" => ServeVendoredFile(context, env, "fraunces-italic-variable-latin.woff2"),
            "source-sans-3-variable-latin.woff2" => ServeVendoredFile(context, env, "source-sans-3-variable-latin.woff2"),
            "jetbrains-mono-variable-latin.woff2" => ServeVendoredFile(context, env, "jetbrains-mono-variable-latin.woff2"),
            "grenze-gotisch-variable-latin.woff2" => ServeVendoredFile(context, env, "grenze-gotisch-variable-latin.woff2"),
            _ => ServeInstalledFaceOrNotFound(file, context, installedFonts),
        };

    /// <summary><paramref name="fileName"/> is always a literal chosen by the switch above — never
    /// the raw request segment — so this never opens a path-traversal surface regardless of what a
    /// caller puts in the URL.</summary>
    static IResult ServeVendoredFile(HttpContext context, IWebHostEnvironment env, string fileName)
    {
        ApplyServingHeaders(context);
        var fullPath = Path.Combine(env.ContentRootPath, "wwwroot", "fonts", fileName);
        return Results.File(fullPath, FontContentType);
    }

    /// <summary>
    /// The T200 fall-through once <paramref name="file"/> misses every vendored literal above —
    /// <see cref="InstalledFontCatalog.TryGetFace"/> is a synchronous, in-memory lookup (never a
    /// filesystem path or a per-request store query), so this costs exactly what
    /// <see cref="ServeVendoredFile"/> does for a request that misses. A directive-identical serving
    /// posture to <see cref="ServeVendoredFile"/> (same <see cref="ApplyServingHeaders"/> call, same
    /// <see cref="FontContentType"/>) is the point of SPEC F104.6's "closed set". One measured
    /// divergence (T200 review): the vendored PhysicalFile arm emits <c>Last-Modified</c>; this byte
    /// arm emits no validator — immutable + 1-year max-age makes revalidation unreachable for a
    /// conforming client, so the directives govern. A sha256-derived ETag on BOTH arms (the store and
    /// fonts-provenance.json both hold the hashes) is the recorded T209 close if true wire parity is
    /// ever wanted.
    /// </summary>
    static IResult ServeInstalledFaceOrNotFound(string file, HttpContext context, InstalledFontCatalog installedFonts)
    {
        if (!installedFonts.TryGetFace(file, out var content))
            return Results.NotFound();

        ApplyServingHeaders(context);
        return Results.File(content.Bytes, FontContentType);
    }

    /// <summary>The vendored caching posture (SPEC F104.6 "identical to vendored"), applied
    /// identically regardless of which half of the closed set is about to serve.</summary>
    static void ApplyServingHeaders(HttpContext context)
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
    }
}
