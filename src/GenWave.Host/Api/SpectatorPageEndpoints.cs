using Microsoft.AspNetCore.Hosting;
using Microsoft.Net.Http.Headers;

namespace GenWave.Host.Api;

/// <summary>
/// Serves the spectator single-page app (SPEC F63.1–F63.5, STORY-173) — hand-written HTML/CSS/JS
/// in <c>wwwroot/spectator</c>, no build step — via endpoint routing. Deliberately NOT
/// <c>UseStaticFiles</c>: static-file middleware runs outside endpoint routing and carries no
/// endpoint metadata, so <see cref="SurfaceGateMiddleware"/> could never 404 it when
/// <c>Station:SpectatorMode</c> is off, and the public-listener isolation check (SPEC F64.1/
/// F64.2) could never recognise it as spectator-surface traffic either — both would be silently
/// wrong (T03/T15 review finding). Every route here carries the same
/// <see cref="SpectatorSurfaceAttribute"/> + <see cref="AuthorizationPolicies.Spectator"/> pair
/// <see cref="SpectatorController"/>'s API routes carry, so the page is gated identically. The
/// vendored <c>.woff2</c> faces the page's own <c>styles.css</c> references are NOT among these
/// routes as of PLAN T173 — they moved to the canonical, surface-shared <c>GET /fonts/{file}</c>
/// (<see cref="FontEndpoints"/>).
/// <para>
/// Every asset route matches exactly one path segment and switches on a literal, known
/// filename — the request-supplied segment is only ever compared for equality, never
/// concatenated into a filesystem path, so there is no path-traversal surface even though the
/// files themselves are served straight off disk.
/// </para>
/// </summary>
static class SpectatorPageEndpoints
{
    /// <summary>Matches the page's own <c>[SpectatorCacheControl]</c> cadence conventions
    /// (SPEC F62.10/F62.11): the page is the most likely to change, assets rarely do.</summary>
    const int PageMaxAgeSeconds = 60;
    const int AssetMaxAgeSeconds = 300;

    const string JavaScriptContentType = "text/javascript; charset=utf-8";
    const string StylesheetContentType = "text/css; charset=utf-8";
    const string IconContentType = "image/x-icon";
    const string PngContentType = "image/png";

    // gh-#160: HEAD rides every route GET is mapped on (RFC 9110 §9.3.2 — same status/headers,
    // body suppressed by the server), through the SAME surface gate and authorization, so a
    // preflighting client reads the truth instead of a routing 404.
    static readonly string[] GetAndHead = ["GET", "HEAD"];

    public static void MapSpectatorPage(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/spectator", GetAndHead, ServeIndex)
            .WithMetadata(new SpectatorSurfaceAttribute())
            .RequireAuthorization(AuthorizationPolicies.Spectator);

        app.MapMethods("/spectator/{asset}", GetAndHead, ServeAsset)
            .WithMetadata(new SpectatorSurfaceAttribute())
            .RequireAuthorization(AuthorizationPolicies.Spectator);
    }

    static IResult ServeIndex(HttpContext context, IWebHostEnvironment env) =>
        ServeFile(context, env, "index.html", "text/html; charset=utf-8", PageMaxAgeSeconds);

    static IResult ServeAsset(string asset, HttpContext context, IWebHostEnvironment env) =>
        asset switch
        {
            "app.js" => ServeFile(context, env, "app.js", JavaScriptContentType, AssetMaxAgeSeconds),
            // The theme switcher's own script (SPEC F102.9/F102.10, STORY-266, PLAN T166) — it
            // fetches GET /spectator/api/themes itself; nothing here templates the catalog into it.
            "switcher.js" => ServeFile(context, env, "switcher.js", JavaScriptContentType, AssetMaxAgeSeconds),
            "styles.css" => ServeFile(context, env, "styles.css", StylesheetContentType, AssetMaxAgeSeconds),
            "favicon.ico" => ServeFile(context, env, "favicon.ico", IconContentType, AssetMaxAgeSeconds),
            // The card-sized station mark (gh-#258): a 256px PNG derived from the operator's
            // GenWave-logo.png (byte-identical to admin-ui/app/icon.png, exactly the favicon's own
            // provenance discipline). The favicon.ico above is a 16/32px tab icon — upscaling it to
            // the 72px now-playing art slot is what looked fuzzy; art slots must use this instead.
            "logo.png" => ServeFile(context, env, "logo.png", PngContentType, AssetMaxAgeSeconds),
            _ => Results.NotFound(),
        };

    /// <summary>
    /// Stamps the shared spectator <c>Cache-Control: public, max-age=N</c> shape (matching
    /// <see cref="SpectatorCacheControlAttribute"/>'s convention for the API surface) and streams
    /// one file from <c>wwwroot/spectator</c>. <paramref name="relativePath"/> is always a literal
    /// chosen by the caller's switch above — never the raw request segment — so this never opens a
    /// path-traversal surface regardless of what a caller puts in the URL.
    /// </summary>
    static IResult ServeFile(HttpContext context, IWebHostEnvironment env, string relativePath, string contentType, int maxAgeSeconds)
    {
        context.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromSeconds(maxAgeSeconds),
        };

        var fullPath = Path.Combine(env.ContentRootPath, "wwwroot", "spectator", relativePath);
        return Results.File(fullPath, contentType);
    }
}
