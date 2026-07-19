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
/// <see cref="SpectatorController"/>'s API routes carry, so the page is gated identically.
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
    const string FontContentType = "font/woff2";

    public static void MapSpectatorPage(this IEndpointRouteBuilder app)
    {
        app.MapGet("/spectator", ServeIndex)
            .WithMetadata(new SpectatorSurfaceAttribute())
            .RequireAuthorization(AuthorizationPolicies.Spectator);

        app.MapGet("/spectator/{asset}", ServeAsset)
            .WithMetadata(new SpectatorSurfaceAttribute())
            .RequireAuthorization(AuthorizationPolicies.Spectator);

        // Vendored woff2 fonts (design-aesthetic skill: never a font CDN request) — a separate
        // route because they live one segment deeper, under wwwroot/spectator/fonts.
        app.MapGet("/spectator/fonts/{asset}", ServeFont)
            .WithMetadata(new SpectatorSurfaceAttribute())
            .RequireAuthorization(AuthorizationPolicies.Spectator);
    }

    static IResult ServeIndex(HttpContext context, IWebHostEnvironment env) =>
        ServeFile(context, env, "index.html", "text/html; charset=utf-8", PageMaxAgeSeconds);

    static IResult ServeAsset(string asset, HttpContext context, IWebHostEnvironment env) =>
        asset switch
        {
            "app.js" => ServeFile(context, env, "app.js", JavaScriptContentType, AssetMaxAgeSeconds),
            "styles.css" => ServeFile(context, env, "styles.css", StylesheetContentType, AssetMaxAgeSeconds),
            _ => Results.NotFound(),
        };

    static IResult ServeFont(string asset, HttpContext context, IWebHostEnvironment env) =>
        asset switch
        {
            "Fraunces-Variable-latin.woff2" =>
                ServeFile(context, env, "fonts/Fraunces-Variable-latin.woff2", FontContentType, AssetMaxAgeSeconds),
            "Fraunces-Italic-Variable-latin.woff2" =>
                ServeFile(context, env, "fonts/Fraunces-Italic-Variable-latin.woff2", FontContentType, AssetMaxAgeSeconds),
            "SourceSans3-Variable-latin.woff2" =>
                ServeFile(context, env, "fonts/SourceSans3-Variable-latin.woff2", FontContentType, AssetMaxAgeSeconds),
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
