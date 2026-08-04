using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using GenWave.Host.Options;
using GenWave.Host.Theming;

namespace GenWave.Host.Api;

/// <summary>
/// <c>GET /spectator/api/themes</c> — the switcher's theme-list read (SPEC F102.10/F102.10a,
/// STORY-266, PLAN T166; supersedes an earlier design that server-templated the catalog into
/// <c>index.html</c> — see <see cref="SpectatorPageEndpoints"/>'s own remarks: that page stays a
/// byte-for-byte static file). Returns exactly <c>{active, options:[{slug, name}]}</c> —
/// <see cref="SpectatorThemesResponse"/> carries nothing else, and the disclosure contract
/// (SPEC F62.9, STORY-183) pins that shape.
///
/// <para>
/// <b>Resolution seam.</b> <see cref="ThemeCatalog.Resolve"/> — the SAME cookie →
/// <c>Station:Theme</c> → shipped-default cascade <see cref="SpectatorThemeEndpoints"/>'s
/// <c>theme.css</c> uses — decides <see cref="SpectatorThemesResponse.Active"/>, so the
/// switcher's pre-selected option always agrees with whichever sheet actually styled this same
/// visitor's page. <see cref="SpectatorThemesResponse.Options"/> is <see cref="ThemeCatalog.All"/>
/// projected to slug/name only.
/// </para>
///
/// <para>
/// <b>Surface gate, authorization, rate limit.</b> Same triple as every other
/// <c>/spectator/api/*</c> read: <see cref="SpectatorSurfaceAttribute"/> (gated by
/// <c>Station:SpectatorMode</c>, public-listener-safe), <see cref="AuthorizationPolicies.Spectator"/>
/// (demands nothing), <see cref="RateLimiterPolicies.Spectator"/> (the 120/minute-per-IP budget
/// <see cref="SpectatorController"/>'s actions share) — applied explicitly here via
/// <c>RequireRateLimiting</c> because this route is minimal-API, not an MVC controller action
/// carrying the class-wide <c>[EnableRateLimiting]</c> the way <see cref="SpectatorController"/>
/// does.
/// </para>
///
/// <para>
/// <b>Caching — identical contract to <see cref="SpectatorThemeEndpoints"/>'s <c>theme.css</c>.</b>
/// <c>Station:Theme</c> is a live setting (SPEC F102.14): a <c>PUT</c> must reach the very next
/// request with no api restart, which rules out a long <c>max-age</c>. <c>Cache-Control: no-cache</c>
/// plus a content-hash <c>ETag</c> forces revalidation on every cache while still avoiding a
/// re-sent body for an unchanged payload, via 304 — and because <c>active</c> genuinely varies by
/// the request's <c>Cookie</c> header, this route emits <c>Vary: Cookie</c> too, so a shared cache
/// keys on the visitor rather than reusing one entry for everyone.
/// </para>
/// </summary>
static class SpectatorThemesEndpoint
{
    const string JsonContentType = "application/json; charset=utf-8";

    static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    // gh-#160: HEAD rides every route GET is mapped on (RFC 9110 §9.3.2), through the same surface
    // gate, authorization, and rate limit — matches SpectatorThemeEndpoints/SpectatorPageEndpoints.
    static readonly string[] GetAndHead = ["GET", "HEAD"];

    public static void MapSpectatorThemesEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/spectator/api/themes", GetAndHead, ServeThemes)
            .WithMetadata(new SpectatorSurfaceAttribute())
            .RequireAuthorization(AuthorizationPolicies.Spectator)
            .RequireRateLimiting(RateLimiterPolicies.Spectator);
    }

    static IResult ServeThemes(HttpContext context, ThemeCatalog catalog, IOptionsMonitor<StationOptions> stationOptions)
    {
        var active = catalog.Resolve(
            cookieSlug: context.Request.Cookies[ThemeCatalog.CookieName],
            stationSlug: stationOptions.CurrentValue.Theme);

        var payload = new SpectatorThemesResponse(
            active.Slug,
            catalog.All.Select(theme => new SpectatorThemeOption(theme.Slug, theme.Name)).ToList());
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        var etag = new EntityTagHeaderValue($"\"{ComputeContentHash(json)}\"");

        // The response body now depends on the request's Cookie header (same as theme.css) — a
        // shared cache must revalidate per visitor, not serve one visitor's active theme to another.
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

        return Results.Text(json, JsonContentType);
    }

    static string ComputeContentHash(string json) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
