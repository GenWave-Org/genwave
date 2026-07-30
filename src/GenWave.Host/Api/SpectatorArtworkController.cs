using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using GenWave.Core.Abstractions;
using GenWave.Host.Artwork;

namespace GenWave.Host.Api;

/// <summary>
/// <c>GET /spectator/api/artwork/{token}</c> — per-track cover art (SPEC F88.3, STORY-222, PLAN
/// T84). A dedicated controller rather than joining <see cref="SpectatorController"/>: every
/// other spectator route there shares one JSON-projection caching profile
/// (<see cref="SpectatorOutputCachePolicies"/> + <see cref="SpectatorCacheControlAttribute"/>'s
/// short TTLs), while this route serves a binary file with a year-long immutable TTL — a
/// genuinely different caching contract deserves its own type rather than an odd one out beside
/// four unrelated ones (POLA).
/// <para>
/// Carries the same two gates every spectator endpoint needs (<see cref="SpectatorSurfaceAttribute"/>,
/// <see cref="AuthorizationPolicies.Spectator"/>) and the same class-wide
/// <see cref="RateLimiterPolicies.Spectator"/> budget <see cref="SpectatorController"/> uses —
/// this is still spectator-surface traffic, gated and throttled identically.
/// </para>
/// <para>
/// No-oracle discipline (SPEC F88.3): an unknown token, a malformed token (rejected by
/// <see cref="IArtworkTokenStore.ResolveAsync"/> before any database round trip), a resolved
/// track with no embedded art, and an extraction failure ALL fall through to
/// <see cref="ServeStationIcon"/> — the exact same 200, the exact same bytes, the exact same
/// headers, from the exact same file on every call. A prober watching only this response can
/// never learn which of those four reasons produced it, which is the whole point: token
/// existence stays exactly as unguessable as <see cref="IArtworkTokenStore"/> already makes it,
/// and this endpoint adds no second way to test a guess.
/// </para>
/// </summary>
[ApiController]
[Route("spectator/api")]
[SpectatorSurface]
[Authorize(Policy = AuthorizationPolicies.Spectator)]
[EnableRateLimiting(RateLimiterPolicies.Spectator)]
public sealed class SpectatorArtworkController(
    IArtworkTokenStore tokenStore,
    ArtworkService artworkService,
    IWebHostEnvironment env) : ControllerBase
{
    /// <summary>SPEC F88.3: a year, expressed in seconds — paired with the response's
    /// <c>immutable</c> directive, matching the endpoint's own disk-cache-once contract.</summary>
    const int ImmutableMaxAgeSeconds = 31536000;

    /// <summary>The fallback's own budget: one day, and deliberately NOT <c>immutable</c>. The
    /// per-token cover jpegs above genuinely are immutable (extracted once, content lives and dies
    /// with the token), but the station icon is a mutable station asset served under those same
    /// token URLs — gh-#258 itself changed it, and every browser that had cached the old fuzzy
    /// favicon bytes under a year-long <c>immutable</c> kept rendering them long after the server
    /// was fixed (v2.8.10 field report: safe-loop cards stayed fuzzy while DJ-break cards, on the
    /// never-before-cached /spectator/logo.png path, went sharp). One day bounds that staleness
    /// without touching the F88.3 no-oracle discipline: all four fallback reasons still serve the
    /// exact same bytes and headers as each other.</summary>
    const int FallbackMaxAgeSeconds = 86400;

    const string JpegContentType = "image/jpeg";

    /// <summary>The bytes every fallback path serves (SPEC F88.3) — the same card-sized station
    /// mark <see cref="SpectatorPageEndpoints"/> already serves at <c>/spectator/logo.png</c>, so
    /// the station's one visual identity is reused rather than duplicated. logo.png (256px, from
    /// the operator's GenWave-logo.png), NOT the 32px favicon.ico this fallback served before
    /// gh-#258 — art slots render at 72px CSS (2-3x that in device pixels), where the upscaled
    /// favicon was visibly fuzzy next to real ≤500px cover extractions.</summary>
    const string StationIconContentType = "image/png";

    [HttpGet("artwork/{token}")]
    [HttpHead("artwork/{token}")]   // gh-#160: HEAD answers with GET's exact status/headers, body suppressed by the server
    public async Task<IActionResult> GetArtwork(string token, CancellationToken ct)
    {
        var resolution = await tokenStore.ResolveAsync(token, ct);
        if (resolution is not null)
        {
            var jpegPath = await artworkService.GetOrExtractAsync(token, resolution.Path, ct);
            if (jpegPath is not null)
                return ServeImmutable(jpegPath, JpegContentType);
        }

        return ServeStationIcon();
    }

    IActionResult ServeStationIcon()
    {
        var iconPath = Path.Combine(env.ContentRootPath, "wwwroot", "spectator", "logo.png");
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromSeconds(FallbackMaxAgeSeconds),
        };
        return PhysicalFile(iconPath, StationIconContentType);
    }

    IActionResult ServeImmutable(string path, string contentType)
    {
        var cacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromSeconds(ImmutableMaxAgeSeconds),
        };
        cacheControl.Extensions.Add(new NameValueHeaderValue("immutable"));
        Response.GetTypedHeaders().CacheControl = cacheControl;

        return PhysicalFile(path, contentType);
    }
}
