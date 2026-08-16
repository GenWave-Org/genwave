using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Artwork;
using GenWave.Host.Images;

namespace GenWave.Host.Api;

/// <summary>
/// The station image's own write paths (SPEC F131.1, STORY-339, PLAN T307): <c>PUT</c>/<c>DELETE
/// /api/station/image</c>, plus <c>GET</c> for the authed console's own read (the persona-avatar
/// parity: mirrors <see cref="PersonaAvatarController"/>'s own <c>GET</c>/<c>PUT</c>/<c>DELETE</c>
/// shape one-for-one, minus <c>POST .../from-pack</c> — a station image has no catalog-acquisition
/// path, upload/delete only). A SIBLING controller, not a member of any existing one — the station
/// image's own small, cohesive lifecycle, the exact same "its own controller" reasoning
/// <see cref="PersonaAvatarController"/>'s own class remarks give for sitting beside
/// <see cref="PersonaController"/> rather than inside it.
///
/// <para>
/// <b>POLICY PARITY.</b> <see cref="AdminSurfaceAttribute"/> + <see cref="AuthorizationPolicies.Settings"/>
/// — the identical pairing <see cref="PersonaAvatarController"/> carries class-wide, for the identical
/// reason: the station's own image is part of the SAME operator-editable identity surface as
/// <c>Station:Name</c>/<c>Station:PublicBaseUrl</c> and every other Live setting.
/// </para>
///
/// <para>
/// <b>NO OBJECT-LEVEL EXISTENCE CHECK ANYWHERE ON THIS CONTROLLER.</b> Unlike
/// <see cref="PersonaAvatarController"/>, there is no owning row to look up first —
/// <c>station.station_image</c> is a genuine singleton (<see cref="IStationImageStore"/>'s own
/// remarks: <c>id int primary key default 1 check (id = 1)</c>), so every action here operates on
/// "the one row" directly; a foreign-key violation this store's own upsert could ever produce simply
/// does not exist as a failure shape (there is no parent row a station image could reference and
/// fail to find).
/// </para>
///
/// <para>
/// <b>TOKEN ENTROPY (PLAN T290/T295/T307 rider).</b> <see cref="GenerateToken"/> mints a fresh
/// 128-bit cryptographically random hex token (<see cref="RandomNumberGenerator.GetHexString(int, bool)"/>)
/// on every write this controller performs — the SAME idiom
/// <see cref="PersonaAvatarController.GenerateToken"/> already established, minted here rather than
/// inside <see cref="IStationImageStore"/> (a deliberately DUMB store — its own remarks): token
/// policy lives at the write path, on purpose, for the identical "the whole security property of the
/// capability URL" reasoning that type's own remarks give.
/// </para>
/// </summary>
[ApiController]
[Route("api/station/image")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class StationImageController(
    IStationImageStore stationImageStore,
    StationImageCache stationImageCache,
    ImageNormalizeService imageNormalizeService,
    ILogger<StationImageController> logger) : ControllerBase
{
    // 32 lowercase hex chars = 16 bytes = 128 bits (SPEC F131.1) — mirrors
    // PersonaAvatarController.TokenLength verbatim; the two token spaces are unrelated capability
    // spaces that only happen to share the same shape.
    const int TokenLength = 32;

    /// <summary>
    /// GET /api/station/image — the station image's own bytes, for the authed console (SPEC F131.3's
    /// own "authenticated admin pages swap their tab icon" consumer: the session-loaded layout reads
    /// this route to learn whether — and under what token — to embed its own <c>&lt;link rel="icon"&gt;</c>).
    /// AdminSurface+Settings gates this exactly like every other action here — the SAME "authenticated
    /// admin plane, never the public door" posture <see cref="PersonaAvatarController.Get"/>'s own
    /// remarks establish; the public, anonymous byte route is
    /// <c>SpectatorArtworkController.GetStationArtwork</c> instead (F131.4: the image is public by
    /// definition, but this ADMIN route still carries no anonymous access of its own — AC4's "no
    /// anonymous byte route on the admin surface").
    ///
    /// <para>
    /// <b>ETAG, PRIVATE, NO-CACHE — mirrors <see cref="PersonaAvatarController.Get"/>'s own reasoning
    /// verbatim.</b> This URL is stable (scoped by the route itself, not a token) and keeps resolving
    /// to whatever image is CURRENTLY set — the opposite of the public token route's immutable
    /// contract — so this response is never <c>immutable</c>: <c>Cache-Control: private, no-cache</c>
    /// paired with the row's own token as the <see cref="EntityTagHeaderValue"/> lets the framework's
    /// own conditional-request handling answer a matching <c>If-None-Match</c> with a bodyless 304
    /// without re-sending the same ≤768 KiB PNG on every render.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var image = await stationImageStore.GetAsync(ct);
        if (image is null)
            return NotFound(NoStationImageToServeProblem());

        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Private = true,
            NoCache = true,
        };
        // Admin plane carries no CSP (gh-#346) — nosniff stamped directly here, the same precedent
        // PersonaAvatarController.Get/CatalogController.AssetFileResult/FontEndpoints already establish.
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image.Bytes, "image/png", lastModified: null, entityTag: new EntityTagHeaderValue($"\"{image.Token}\""));
    }

    /// <summary>
    /// PUT /api/station/image — accepts a raw <c>image/png</c> or <c>image/jpeg</c> body (SPEC
    /// F131.1). Gate order mirrors <see cref="PersonaAvatarController.Put"/> exactly, minus the
    /// object-level existence check this controller's own class remarks explain away: bounded read
    /// (≤ <see cref="ImageNormalizeService.MaxInputBytes"/>) → the full T291 normalize pipeline →
    /// a fresh token (<see cref="GenerateToken"/>) → <see cref="IStationImageStore.UpsertAsync"/>.
    /// Any failure at any gate is a quiet 400 (<see cref="ImageNormalizeProblemMapper.ToProblem"/>)
    /// and writes nothing: the PREVIOUS image (if any) survives untouched, since
    /// <see cref="IStationImageStore.UpsertAsync"/> is never reached on a failing path.
    /// </summary>
    [HttpPut]
    [RequestSizeLimit(ImageNormalizeService.MaxInputBytes)]
    public async Task<IActionResult> Put(CancellationToken ct)
    {
        var (bytes, oversized) = await BoundedImportBodyReader.ReadBoundedBytesAsync(
            Request, ImageNormalizeService.MaxInputBytes, ct);
        if (oversized)
            return BadRequest(ImageNormalizeProblemMapper.ToProblem(ImageNormalizeFailureReason.TooLarge));

        var normalized = await imageNormalizeService.NormalizeAsync(bytes, ct);
        switch (normalized)
        {
            case ImageNormalizeResult.Failure failure:
                logger.LogInformation("Station image upload rejected reason={Reason}", failure.Reason);
                return BadRequest(ImageNormalizeProblemMapper.ToProblem(failure.Reason));

            case ImageNormalizeResult.Success success:
                await stationImageStore.UpsertAsync(
                    new StationImageInput(success.Bytes, success.Sha256, GenerateToken()), ct);
                // PLAN T307 fix round R1: the write is visible to THIS station's shared
                // StationImageCache singleton before this action returns — every other reader (the
                // feeder push path, the spectator fallback ladder, the admin console's own
                // GET /api/stations snapshot) sees it on their very next call, not merely once
                // StationImageCache.StalenessBound happens to elapse.
                stationImageCache.Invalidate();
                logger.LogInformation("Station image uploaded");
                return await ToDtoResultAsync(ct);

            default:
                throw new UnreachableException($"Unhandled {nameof(ImageNormalizeResult)} case.");
        }
    }

    /// <summary>
    /// DELETE /api/station/image — removes the customized image, if any (SPEC F131.1: reverts the
    /// F88 fallback, the feeder stamp, the spectator favicon/logo, and the admin tab icon alike to
    /// the shipped logo, byte-identical to a station that never uploaded — F131.5's own observable
    /// contract). Deliberately NO separate existence pre-check — mirrors
    /// <see cref="PersonaAvatarController.Delete"/>'s own reasoning: <see cref="IStationImageStore.DeleteAsync"/>'s
    /// own contract already reports "was there a row to delete" as a plain <see langword="bool"/>.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        var deleted = await stationImageStore.DeleteAsync(ct);
        if (!deleted)
            return NotFound(NoStationImageProblem());

        // PLAN T307 fix round R1 — see Put's own identical call for why.
        stationImageCache.Invalidate();
        logger.LogInformation("Station image removed");
        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>128-bit cryptographically random hex, freshly minted for every write — see this
    /// class's own TOKEN ENTROPY remarks for why no collision handling lives here.</summary>
    static string GenerateToken() => RandomNumberGenerator.GetHexString(TokenLength, lowercase: true);

    /// <summary>
    /// Re-reads the just-written row so the response reports what was actually persisted — mirrors
    /// <see cref="PersonaAvatarController"/>'s own <c>ToDtoResultAsync</c> idiom, INCLUDING its own
    /// T295-review rider: a concurrent <c>DELETE /api/station/image</c> landing between this
    /// method's caller's own <see cref="IStationImageStore.UpsertAsync"/> and this re-read is a real,
    /// reachable race, downgraded to the SAME honest 404 <see cref="Get"/> already reports for "no
    /// image right now" rather than an <see cref="UnreachableException"/> — the write itself already
    /// happened and already logged.
    /// </summary>
    async Task<IActionResult> ToDtoResultAsync(CancellationToken ct)
    {
        var image = await stationImageStore.GetAsync(ct);
        if (image is null)
            return NotFound(NoStationImageToServeProblem());

        return Ok(new StationImageDto(image.Token, image.UpdatedAt));
    }

    static ProblemDetails NoStationImageProblem() => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = "The station has no customized image to remove.",
    };

    /// <summary>Shared by <see cref="Get"/> (no image right now) and <see cref="ToDtoResultAsync"/>
    /// (no image any more, by the time this re-read the row it just wrote — see that method's own
    /// remarks). Deliberately not <see cref="NoStationImageProblem"/>: that one's own wording is
    /// DELETE-specific ("to remove"), which would misdescribe a request that never asked to remove
    /// anything — mirrors <see cref="PersonaAvatarController"/>'s own equivalent split for its worn
    /// face.</summary>
    static ProblemDetails NoStationImageToServeProblem() => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = "The station has no customized image to serve.",
    };
}
