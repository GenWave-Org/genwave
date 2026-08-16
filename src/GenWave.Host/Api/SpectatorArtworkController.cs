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
/// T84) — and <c>GET /spectator/api/artwork/dj/{token}</c> — the on-air persona's worn face
/// (SPEC F129.1, STORY-335, PLAN T298). One controller, not two: the two token spaces share every
/// gate/rate-limit/serving idiom below, and PLAN.md's own T298 line notes the forthcoming
/// station-token route (T307) rides this SAME controller rather than duplicating the idiom a
/// third time.
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
    IPersonaAvatarStore personaAvatarStore,
    IStationImageStore stationImageStore,
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

    /// <summary>The owner-customized station image read back from <see cref="IStationImageStore"/>
    /// (SPEC F131) — <see cref="StationImage"/>'s own remarks: "the stored 512x512 normalized PNG,
    /// metadata-free." Same value as <see cref="StationIconContentType"/> and
    /// <see cref="PersonaAvatarContentType"/>, but a THIRD named constant on purpose: this codebase
    /// serves three distinct assets under this controller (shipped logo file, owner-uploaded
    /// station-image row, persona-avatar row) that only happen to share a PNG encoding — reusing
    /// <see cref="StationIconContentType"/> here would stamp the row bytes with a constant whose own
    /// doc comment describes a different file entirely.</summary>
    const string OwnerStationImageContentType = "image/png";

    /// <summary>The worn face is always a normalized PNG (<see cref="IPersonaAvatarStore"/>'s own
    /// remarks — the T291 pipeline's fixed output format), same value as
    /// <see cref="StationIconContentType"/> but kept as its own named constant: the two describe
    /// different assets that only happen to share an encoding.</summary>
    const string PersonaAvatarContentType = "image/png";

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
    /// <b>THE FALLBACK IS NEVER <c>immutable</c> — mirrors <see cref="ServeStationIcon"/>'s own
    /// gh-#258 lesson exactly, for the same reason.</b> A resolved face is immutable-safe because
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
            return ServeImmutableBytes(avatar.Bytes, PersonaAvatarContentType);

        return await ServeStationImageAsync(ct);
    }

    /// <summary>Serves the SHIPPED station mark — the on-disk <c>wwwroot/spectator/logo.png</c>
    /// file (gh-#258), never the owner-uploaded row <see cref="ServeStationImageAsync"/> reads.
    /// <see cref="GetArtwork"/>'s own fallback and <see cref="ServeStationImageAsync"/>'s OWN
    /// last-resort both land here — the one file every fallback path ultimately serves when no row
    /// exists to prefer.</summary>
    IActionResult ServeStationIcon()
    {
        var iconPath = Path.Combine(env.ContentRootPath, "wwwroot", "spectator", "logo.png");
        SetFallbackCacheControl();
        return PhysicalFile(iconPath, StationIconContentType);
    }

    /// <summary>
    /// <see cref="GetDjArtwork"/>'s own fallback (PLAN T298 build note) — NOT a synonym for
    /// <see cref="ServeStationIcon"/> despite the near-homophone name: this method reads
    /// <see cref="IStationImageStore"/> first — the owner-customized station-image ROW, when one
    /// has been uploaded — and only calls <see cref="ServeStationIcon"/> (the shipped FILE) when
    /// that row does not exist. <see cref="GetArtwork"/>'s OWN fallback above stays shipped-file-
    /// only until T307 rewires it (that task's own PLAN line: "the F88 artwork fallback reads
    /// row-else-shipped-logo"); this route reads the row one task early because
    /// <see cref="IStationImageStore"/> already exists (T290) and the call is one cheap singleton
    /// read, not a pipeline this task would otherwise have to duplicate at T307 — the F131.2 "every
    /// consumer" promise is already true here rather than merely true-by-T307. Full unification of
    /// the two fallback paths is T307's own scope, not this one's.
    /// </summary>
    async Task<IActionResult> ServeStationImageAsync(CancellationToken ct)
    {
        var stationImage = await stationImageStore.GetAsync(ct);
        if (stationImage is null)
            return ServeStationIcon();

        SetFallbackCacheControl();
        return File(stationImage.Bytes, OwnerStationImageContentType);
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
