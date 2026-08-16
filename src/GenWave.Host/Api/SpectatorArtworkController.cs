using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Artwork;

namespace GenWave.Host.Api;

/// <summary>
/// <c>GET /spectator/api/artwork/{token}</c> — per-track cover art (SPEC F88.3, STORY-222, PLAN
/// T84); <c>GET /spectator/api/artwork/dj/{token}</c> — the on-air persona's worn face (SPEC
/// F129.1, STORY-335, PLAN T298); and <c>GET /spectator/api/artwork/station/{token}</c> — the
/// station's own customized image (SPEC F131.2, STORY-339, PLAN T307). One controller, not three:
/// all three token spaces share every gate/rate-limit/serving idiom below.
/// <para>
/// A dedicated controller rather than joining <see cref="SpectatorController"/>: every
/// other spectator route there shares one JSON-projection caching profile
/// (<see cref="SpectatorOutputCachePolicies"/> + <see cref="SpectatorCacheControlAttribute"/>'s
/// short TTLs), while these routes serve binary files/bytes with a year-long immutable TTL — a
/// genuinely different caching contract deserves its own type rather than an odd one out beside
/// four unrelated ones (POLA).
/// </para>
/// <para>
/// Carries the same two gates every spectator endpoint needs (<see cref="SpectatorSurfaceAttribute"/>,
/// <see cref="AuthorizationPolicies.Spectator"/>) and the same class-wide
/// <see cref="RateLimiterPolicies.Spectator"/> budget <see cref="SpectatorController"/> uses —
/// this is still spectator-surface traffic, gated and throttled identically.
/// </para>
/// <para>
/// No-oracle discipline (SPEC F88.3): an unknown token, a malformed token (rejected by
/// <see cref="IArtworkTokenStore.ResolveAsync"/>/<see cref="ArtworkToken.IsWellFormed"/> before any
/// database round trip), a resolved track with no embedded art, and an extraction failure ALL fall
/// through to the SAME unified fallback (<see cref="ServeStationImageAsync"/> — row-else-shipped-logo,
/// PLAN T307's own ladder unification) — the exact same 200, the exact same bytes, the exact same
/// headers, for every reason. A prober watching only this response can never learn which reason
/// produced it, which is the whole point: token existence stays exactly as unguessable as the
/// resolving store already makes it, and this endpoint adds no second way to test a guess.
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
    IPersonaAvatarStore personaAvatarStore,
    StationImageCache stationImageCache,
    IWebHostEnvironment env) : ControllerBase
{
    /// <summary>SPEC F88.3: a year, expressed in seconds — paired with the response's
    /// <c>immutable</c> directive, matching the endpoint's own disk-cache-once contract.</summary>
    const int ImmutableMaxAgeSeconds = 31536000;

    /// <summary>The fallback's own budget: one day, and deliberately NOT <c>immutable</c>. The
    /// per-token cover jpegs above genuinely are immutable (extracted once, content lives and dies
    /// with the token), but the station image is a mutable station asset served under those same
    /// token URLs — gh-#258 itself changed it, and every browser that had cached the old fuzzy
    /// favicon bytes under a year-long <c>immutable</c> kept rendering them long after the server
    /// was fixed (v2.8.10 field report: safe-loop cards stayed fuzzy while DJ-break cards, on the
    /// never-before-cached /spectator/logo.png path, went sharp). One day bounds that staleness
    /// without touching the F88.3 no-oracle discipline: all fallback reasons still serve the exact
    /// same bytes and headers as each other.</summary>
    const int FallbackMaxAgeSeconds = 86400;

    const string JpegContentType = "image/jpeg";

    /// <summary>
    /// The one PNG content-type this controller ever stamps (PLAN T307 review rider — cleanup:
    /// three near-identical constants, EACH literally <c>"image/png"</c>, previously distinguished
    /// only by which of three PNG-shaped assets they described — the shipped logo file, the
    /// owner-uploaded station-image row, and a persona-avatar row. Post-T307's ladder unification
    /// those three all funnel through the SAME <see cref="ServeStationImageAsync"/>/<see cref="ServeImmutableBytes"/>
    /// call sites regardless of which asset produced the bytes, so three constants naming the same
    /// value no longer earn their keep — one honest name, used everywhere a PNG goes out.</summary>
    const string PngContentType = "image/png";

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

        // PLAN T307: row-else-shipped-logo, the SAME unified fallback GetDjArtwork/GetStationArtwork
        // fall through to — this ladder no longer diverges by which route missed.
        return await ServeStationImageAsync(ct);
    }

    /// <summary>
    /// <c>GET /spectator/api/artwork/dj/{token}</c> — the on-air persona's worn face (SPEC
    /// F129.1, STORY-335, PLAN T298). A resolved token serves the face bytes with the same
    /// year-long <c>immutable</c> contract <see cref="GetArtwork"/> gives a resolved cover: safe
    /// for the identical reason — <see cref="IPersonaAvatarStore.UpsertAsync"/> rotates the token
    /// on every write (<c>PersonaAvatarController</c>'s own TOKEN ENTROPY remarks), so the token
    /// IS the version and a byte-different face never reuses an old URL.
    /// <para>
    /// <b>NO-ORACLE DISCIPLINE (F129.1, the F88.3 idiom extended).</b> An unknown token, a
    /// STALE token (the face this persona wore before its most recent upload/apply/remove), and a
    /// MALFORMED token (rejected by <see cref="ArtworkToken.IsWellFormed"/> before any store call
    /// — the exact non-enumerability guard <c>ArtworkTokenRepository.ResolveAsync</c> already runs
    /// for cover-art tokens, shared from ONE home rather than a second copy) all read as an
    /// ordinary miss — <see cref="IPersonaAvatarStore.GetByTokenAsync"/>'s own contract already
    /// promises no distinction between unknown and stale — and all three fall through to
    /// <see cref="ServeStationImageAsync"/> here: the same 200, the same bytes, the same headers.
    /// A prober watching only this response can never learn whether a guessed token once existed,
    /// and a malformed guess never even buys a round trip to the store (Postgres in production —
    /// an outage there can never 500 this route for a shape it could have rejected for free).
    /// </para>
    /// <para>
    /// <b>THE FALLBACK IS NEVER <c>immutable</c> — mirrors the station-token route's own gh-#258
    /// lesson exactly, for the same reason.</b> A resolved face is immutable-safe because
    /// rotation re-URLs it; the station image served under a MISSED dj token has the opposite
    /// property — it is a mutable asset reachable under infinitely many URLs (every unknown/stale
    /// token that ever gets tried), so pinning it <c>immutable</c> would let a browser that hit
    /// this route with a since-replaced token keep rendering that stale image for a year after the
    /// owner changed it — the exact regression gh-#258 already taught this codebase to avoid.
    /// </para>
    /// </summary>
    [HttpGet(DjArtworkPaths.RouteSegment)]
    [HttpHead(DjArtworkPaths.RouteSegment)]   // gh-#160 parity: HEAD must answer with GET's exact status/headers
    public async Task<IActionResult> GetDjArtwork(string token, CancellationToken ct)
    {
        // The F88.2 non-enumerability guard, extended to the dj token space (ArtworkToken's own
        // remarks): a malformed token can never justify a personaAvatarStore round trip.
        if (!ArtworkToken.IsWellFormed(token))
            return await ServeStationImageAsync(ct);

        var avatar = await personaAvatarStore.GetByTokenAsync(token, ct);
        if (avatar is not null)
            return ServeImmutableBytes(avatar.Bytes, PngContentType);

        return await ServeStationImageAsync(ct);
    }

    /// <summary>
    /// <c>GET /spectator/api/artwork/station/{token}</c> — the station's own customized image (SPEC
    /// F131.2, STORY-339, PLAN T307) — the token-versioned URL <see cref="Engine.ArtworkUrlResolver"/>
    /// stamps when the station image IS customized (<see cref="StationArtworkPaths.PathPrefix"/>'s
    /// own remarks); the NOT-customized case never reaches this action at all — it resolves through
    /// <see cref="GetArtwork"/>'s own generic route instead (<see cref="StationArtworkPaths.ShippedFallbackPath"/>'s
    /// own remarks explain why no dedicated route is needed for that half).
    /// <para>
    /// <b>NO-ORACLE DISCIPLINE, mirrors <see cref="GetDjArtwork"/> exactly.</b> A malformed token
    /// (<see cref="ArtworkToken.IsWellFormed"/>, before any cache/store read), an unknown token, and
    /// a STALE token (the image this station wore before its most recent upload) all fall through to
    /// the SAME <see cref="ServeStationImageAsync"/> fallback — F131.4's own "unknown station tokens
    /// serve the CURRENT station image bytes with 200 (no oracle, no history)".
    /// </para>
    /// </summary>
    [HttpGet(StationArtworkPaths.RouteSegment)]
    [HttpHead(StationArtworkPaths.RouteSegment)]   // gh-#160 parity: HEAD must answer with GET's exact status/headers
    public async Task<IActionResult> GetStationArtwork(string token, CancellationToken ct)
    {
        if (!ArtworkToken.IsWellFormed(token))
            return await ServeStationImageAsync(ct);

        var stationImage = await stationImageCache.GetAsync(ct);
        if (stationImage is not null && stationImage.Token == token)
            return ServeImmutableBytes(stationImage.Bytes, PngContentType);

        return await ServeStationImageAsync(ct);
    }

    /// <summary>Serves the SHIPPED station mark — the on-disk <c>wwwroot/spectator/logo.png</c>
    /// file (gh-#258) — the LAST-RESORT every fallback path here ultimately serves when no
    /// owner-customized row exists to prefer.</summary>
    IActionResult ServeStationIcon()
    {
        var iconPath = Path.Combine(env.ContentRootPath, "wwwroot", "spectator", "logo.png");
        SetFallbackCacheControl();
        return PhysicalFile(iconPath, PngContentType);
    }

    /// <summary>
    /// THE ONE unified fallback every token-resolution miss on this controller falls through to
    /// (PLAN T307 review rider — GetArtwork and GetDjArtwork no longer diverge; GetStationArtwork's
    /// own miss lands here too): reads the owner-customized station-image ROW through
    /// <see cref="StationImageCache"/> (never <see cref="IStationImageStore"/> directly — the T298
    /// review rider MANDATE this cache exists to satisfy: an anonymous miss at this controller's own
    /// 120/min/IP rate-limit ceiling must never drag the full ~200 KiB <c>bytes</c> column through
    /// Postgres on every single request) and serves it when set, falling to <see cref="ServeStationIcon"/>
    /// (the shipped FILE) only when that row does not exist. SPEC F131.2's own "the F88 artwork
    /// fallback reads row-else-shipped-logo" promise, now true at every fallback site on this
    /// controller — not merely the dj route T298 shipped it on first.
    /// </summary>
    async Task<IActionResult> ServeStationImageAsync(CancellationToken ct)
    {
        var stationImage = await stationImageCache.GetAsync(ct);
        if (stationImage is null)
            return ServeStationIcon();

        SetFallbackCacheControl();
        return File(stationImage.Bytes, PngContentType);
    }

    /// <summary>Shared by every fallback path above (<see cref="ServeStationIcon"/>,
    /// <see cref="ServeStationImageAsync"/>) — the one-day, never-<c>immutable</c> cache posture
    /// <see cref="FallbackMaxAgeSeconds"/>'s own remarks justify, kept in one place so a future
    /// fallback path cannot silently drift from it.</summary>
    void SetFallbackCacheControl() =>
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromSeconds(FallbackMaxAgeSeconds),
        };

    IActionResult ServeImmutable(string path, string contentType)
    {
        SetImmutableCacheControl();
        return PhysicalFile(path, contentType);
    }

    IActionResult ServeImmutableBytes(byte[] bytes, string contentType)
    {
        SetImmutableCacheControl();
        return File(bytes, contentType);
    }

    void SetImmutableCacheControl()
    {
        var cacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromSeconds(ImmutableMaxAgeSeconds),
        };
        cacheControl.Extensions.Add(new NameValueHeaderValue("immutable"));
        Response.GetTypedHeaders().CacheControl = cacheControl;
    }
}
