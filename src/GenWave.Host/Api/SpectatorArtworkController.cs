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

    const string JpegContentType = "image/jpeg";

    /// <summary>The bytes every fallback path serves (SPEC F88.3) — the same file
    /// <see cref="SpectatorPageEndpoints"/> already serves at <c>/spectator/favicon.ico</c>, so
    /// the station's one visual identity is reused rather than duplicated.</summary>
    const string StationIconContentType = "image/x-icon";

    [HttpGet("artwork/{token}")]
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
        var iconPath = Path.Combine(env.ContentRootPath, "wwwroot", "spectator", "favicon.ico");
        return ServeImmutable(iconPath, StationIconContentType);
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
