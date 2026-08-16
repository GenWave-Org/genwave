using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Catalog;
using GenWave.Host.Images;

namespace GenWave.Host.Api;

/// <summary>
/// The worn-face write paths (SPEC F128.5/.6, F129.1; STORY-333; PLAN T295): <c>PUT</c>/<c>DELETE
/// /api/personas/{id}/avatar</c> and <c>POST /api/personas/{id}/avatar/from-pack</c>. A SIBLING
/// controller to <see cref="PersonaController"/> (mirrors how <see cref="AvatarPackController"/> sits
/// beside <see cref="FontPackController"/> rather than growing inside it) — the worn-face lifecycle is
/// its own small, cohesive surface, and <see cref="PersonaController"/> is already the biggest
/// controller in this codebase.
///
/// <para>
/// <b>POLICY PARITY (verified, not assumed).</b> <see cref="PersonaController"/> carries ONE
/// class-level <c>[Authorize(Policy = AuthorizationPolicies.Settings)]</c> covering every one of its
/// actions, including <c>PATCH</c>/<c>DELETE</c> — there is no separate "persona write" policy in this
/// codebase to match; <c>Settings</c> IS the persona-write policy. This controller carries the
/// identical pairing for the identical reason: a face is part of a persona's own editable identity, the
/// same operator surface that already edits its name/backstory/voice.
/// </para>
///
/// <para>
/// <b>OBJECT-LEVEL EXISTENCE CHECK (security-api IDOR discipline).</b> Every action starts by resolving
/// <c>id</c> against <see cref="IPersonaStore.GetByIdAsync"/> — never trusting that a route id which
/// parsed as a <see langword="long"/> also names a real row. Without this gate,
/// <see cref="IPersonaAvatarStore.UpsertAsync"/> would attempt an insert against
/// <c>station.persona_avatar</c>'s own <c>persona_id</c> foreign key (db/37,
/// <c>references station.persona(id)</c>) for a persona that does not exist, surfacing as an unhandled
/// Postgres foreign-key-violation 500 instead of an honest 404.
/// </para>
///
/// <para>
/// <b>TOKEN ENTROPY IS THE WHOLE SECURITY PROPERTY OF THE CAPABILITY URL (PLAN T290/T295 rider).</b>
/// <see cref="GenerateToken"/> mints a fresh 128-bit cryptographically random hex token
/// (<see cref="RandomNumberGenerator.GetHexString(int, bool)"/>, the SAME idiom
/// <c>ArtworkTokenRepository</c> already established for the F88 opaque-token transport) on EVERY write
/// this controller performs — upload, apply-from-pack, AND delete (which simply removes the row, so the
/// next write mints an entirely fresh token rather than reusing anything). <see cref="IPersonaAvatarStore"/>
/// is deliberately a DUMB store (its own remarks): it persists whatever token this controller already
/// chose, never generates or rotates one itself — token policy lives here, at the write path, on
/// purpose. Uniqueness is BY CONSTRUCTION, not by a database round trip: 128 bits of CSPRNG output
/// collides with a second independently-drawn 128-bit value with probability 2⁻¹²⁸ — for any station
/// this software will ever run, that is indistinguishable from "never" (the birthday bound over even
/// billions of rows moves the exponent by a few dozen bits at most), so this controller mints and
/// writes a candidate directly rather than the <c>coalesce(...)  returning</c> race-check
/// <c>ArtworkTokenRepository.GetOrCreateTokenAsync</c> needs for ITS OWN different problem (many
/// concurrent first-readers lazily minting the SAME row's token) — a problem this controller doesn't
/// have, since every write here already knows it is replacing the row outright.
/// </para>
/// </summary>
[ApiController]
[Route("api/personas/{id:long}/avatar")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class PersonaAvatarController(
    IPersonaStore personaStore,
    IPersonaAvatarStore personaAvatarStore,
    IAvatarPackStore avatarPackStore,
    ImageNormalizeService imageNormalizeService,
    ILogger<PersonaAvatarController> logger) : ControllerBase
{
    // 32 lowercase hex chars = 16 bytes = 128 bits (SPEC F129.1) — mirrors
    // ArtworkTokenRepository.TokenLength verbatim; the two never need to be the same CONSTANT (an
    // avatar token and an artwork token are unrelated capability spaces), only the same SHAPE.
    const int TokenLength = 32;

    /// <summary>
    /// GET /api/personas/{id}/avatar — the worn face's own bytes (SPEC F128.9, STORY-333, PLAN T296 —
    /// the Personas-page render decision recorded there: build the admin-plane byte read, since T295
    /// shipped only the write paths and T298's public token route is still unbuilt). AdminSurface+Settings
    /// gates this exactly like every other action in this controller — this is the authed console's OWN
    /// read of a face, never the public door: T298's forthcoming spectator route resolves the SAME bytes
    /// anonymously by opaque token instead, a wholly separate capability-URL surface this action carries
    /// no relationship to beyond serving the same underlying <see cref="IPersonaAvatarStore"/> row.
    ///
    /// <para>
    /// <b>NO OBJECT-LEVEL PRE-CHECK — mirrors <see cref="Delete"/>'s own reasoning exactly.</b> An unknown
    /// persona id and a known persona with no face both read as 404 here, with no separate
    /// <see cref="IPersonaStore.GetByIdAsync"/> round trip to tell them apart:
    /// <see cref="IPersonaAvatarStore.GetByPersonaIdAsync"/> already answers "is there a face to serve" in
    /// one call, and there is nothing a second existence check would protect a READ from the way it
    /// protects <see cref="Put"/>/<see cref="ApplyFromPack"/>'s own writes from a foreign-key violation.
    /// </para>
    ///
    /// <para>
    /// <b>ETAG, NOT A LONG IMMUTABLE CACHE — a deliberate divergence from <see cref="SpectatorArtworkController"/>'s
    /// own year-long <c>immutable</c> contract.</b> A spectator artwork URL is scoped by an opaque,
    /// per-extraction token that never reuses a byte-different payload under the same URL — genuinely
    /// immutable. THIS route is scoped by persona <paramref name="id"/>, a stable value that keeps
    /// resolving to whatever face is CURRENTLY worn — the opposite property: the bytes behind this URL
    /// change the moment an operator uploads, removes, or applies a pack face, with no URL change to
    /// signal it. Marking this response <c>immutable</c> would tell a browser to skip revalidation
    /// entirely for up to a year, silently keeping a stale face on-screen through every write that
    /// follows. <c>Cache-Control: private, no-cache</c> instead means "cache it, but always ask first" —
    /// paired with the <see cref="EntityTagHeaderValue"/> this action hands
    /// <see cref="ControllerBase.File(byte[], string, System.DateTimeOffset?, EntityTagHeaderValue?)"/>,
    /// the framework's own conditional-request handling answers a matching <c>If-None-Match</c> with a bodyless 304 rather
    /// than re-sending the same ≤512 KiB PNG on every render — cheap AND correct, never trading one for
    /// the other. <c>Private</c> (never <c>Public</c>, unlike <see cref="SpectatorArtworkController"/>'s
    /// own anonymous route): this is cookie-authenticated admin content, not a byte stream any
    /// shared/proxy cache should ever be allowed to hold.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var avatar = await personaAvatarStore.GetByPersonaIdAsync(id, ct);
        if (avatar is null)
            return NotFound(NoFaceToServeProblem(id));

        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Private = true,
            NoCache = true,
        };
        // Admin plane carries no CSP (gh-#346) — nosniff stamped directly here, same precedent as
        // CatalogController.AssetFileResult and FontEndpoints.
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(avatar.Bytes, "image/png", lastModified: null, entityTag: new EntityTagHeaderValue($"\"{avatar.Token}\""));
    }

    /// <summary>
    /// PUT /api/personas/{id}/avatar — accepts a raw <c>image/png</c> or <c>image/jpeg</c> body
    /// (SPEC F128.6). <c>Content-Type</c> is advisory only and never gates anything here — the T291
    /// pipeline's own magic-bytes check is the real decision, so this action carries no
    /// <c>[Consumes]</c> restriction. Gate order: bounded read (≤ <see cref="ImageNormalizeService.MaxInputBytes"/>,
    /// via <see cref="BoundedImportBodyReader.ReadBoundedBytesAsync"/> — the raw-bytes sibling of the
    /// portable-import routes' own UTF-8 read, since a PNG/JPEG body is never valid UTF-8 text) → the
    /// object-level persona-existence check (this class's own remarks) → the full T291 normalize
    /// pipeline (magic bytes → header dimensions/APNG → ffmpeg re-encode) → a fresh token
    /// (<see cref="GenerateToken"/>) → <see cref="IPersonaAvatarStore.UpsertAsync"/> with
    /// <see cref="PersonaAvatarSource.Upload"/>. Any failure at any gate is a quiet 400
    /// (<see cref="ImageNormalizeProblemMapper.ToProblem"/> — F15.7, never named beyond the honest
    /// reason text; EXTRACTED to that shared home at PLAN T307's own second-copy moment) and
    /// writes nothing: the PREVIOUS face (if any) survives untouched, since
    /// <see cref="IPersonaAvatarStore.UpsertAsync"/> is never reached on a failing path.
    /// </summary>
    [HttpPut]
    [RequestSizeLimit(ImageNormalizeService.MaxInputBytes)]
    public async Task<IActionResult> Put(long id, CancellationToken ct)
    {
        var (bytes, oversized) = await BoundedImportBodyReader.ReadBoundedBytesAsync(
            Request, ImageNormalizeService.MaxInputBytes, ct);
        if (oversized)
            return BadRequest(ImageNormalizeProblemMapper.ToProblem(ImageNormalizeFailureReason.TooLarge));

        if (await FindMissingPersonaResultAsync(id, ct) is { } missing)
            return missing;

        var normalized = await imageNormalizeService.NormalizeAsync(bytes, ct);
        switch (normalized)
        {
            case ImageNormalizeResult.Failure failure:
                logger.LogInformation(
                    "Persona avatar upload rejected personaId={PersonaId} reason={Reason}", id, failure.Reason);
                return BadRequest(ImageNormalizeProblemMapper.ToProblem(failure.Reason));

            case ImageNormalizeResult.Success success:
                await personaAvatarStore.UpsertAsync(
                    new PersonaAvatarInput(id, success.Bytes, success.Sha256, GenerateToken(), PersonaAvatarSource.Upload, null),
                    ct);
                logger.LogInformation("Persona avatar uploaded personaId={PersonaId}", id);
                return await ToDtoResultAsync(id, ct);

            default:
                throw new UnreachableException($"Unhandled {nameof(ImageNormalizeResult)} case.");
        }
    }

    /// <summary>
    /// POST /api/personas/{id}/avatar/from-pack — copies an installed avatar-pack item's already-
    /// normalized bytes onto a persona (SPEC F128.5). Gate order: the object-level persona-existence
    /// check (this class's own remarks) → both DTO fields present (400, blank/missing — never a raw
    /// model-binding 400, so this shares the same <see cref="ProblemDetails"/> shape every other
    /// field-level rejection in this controller uses) → the pack exists
    /// (<see cref="IAvatarPackStore.GetBySlugAsync"/>, 404) → the pack carries an item with that exact
    /// <c>name</c> (ordinal — mirrors <c>station.avatar_pack_item</c>'s own <c>UNIQUE(pack_id, name)</c>
    /// case-sensitive constraint, db/37; 404) → a fresh token → <see cref="IPersonaAvatarStore.UpsertAsync"/>
    /// with <see cref="PersonaAvatarSource.Catalog"/>, <c>ImportedFrom = packSlug</c>. None of the three
    /// 404s here name which check failed beyond its own ProblemDetails.Detail — this is an
    /// authenticated admin-only surface, not a spectator/anonymous one, so echoing back the slug/item
    /// the operator themselves just typed (mirrors <c>PersonaController.UnknownSlugProblem</c>'s own
    /// precedent) is not an oracle leak the way <see cref="AvatarPackController"/>'s catalog-facing
    /// refusals must stay silent about.
    ///
    /// <para>
    /// <b>NO RE-NORMALIZE (deliberate, documented here per the PLAN T295 build note).</b> An installed
    /// pack item's bytes are ALREADY the normalized derivative — <see cref="AvatarPackController"/>'s
    /// own install route ran every item through this exact <see cref="ImageNormalizeService"/> pipeline
    /// BEFORE ever writing <c>station.avatar_pack_item</c> (its own RE-VALIDATION IS NOT OPTIONAL
    /// remarks). Running the pipeline a second time here would be redundant work with an identical
    /// outcome — the SAME bytes, since ffmpeg's re-encode of an already-512×512, already-<c>-pix_fmt
    /// rgba</c>, already-metadata-stripped PNG is idempotent — so this action CARRIES the item's own
    /// <see cref="AvatarPackItem.Sha256"/> forward rather than recomputing a hash over bytes it never
    /// mutates: the pinned hash still genuinely describes the bytes this write stores (unlike
    /// <see cref="AvatarPackController"/>'s own install route, which recomputes because normalization
    /// there DOES change the bytes it fetched).
    /// </para>
    /// </summary>
    [HttpPost("from-pack")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> ApplyFromPack(long id, [FromBody] PersonaAvatarFromPackRequest request, CancellationToken ct)
    {
        if (await FindMissingPersonaResultAsync(id, ct) is { } missing)
            return missing;

        if (string.IsNullOrWhiteSpace(request.PackSlug) || string.IsNullOrWhiteSpace(request.ItemName))
            return BadRequest(BlankFromPackFieldProblem());

        var pack = await avatarPackStore.GetBySlugAsync(request.PackSlug, ct);
        if (pack is null)
            return NotFound(UnknownPackProblem(request.PackSlug));

        var item = pack.Items.FirstOrDefault(i => i.Name == request.ItemName);
        if (item is null)
            return NotFound(UnknownItemProblem(request.PackSlug, request.ItemName));

        // T295-review rider: persist the pack's own CANONICAL Slug, not the caller-typed
        // request.PackSlug — GetBySlugAsync already resolved the two as the same installed row, but
        // the stored provenance should name what the pack actually IS, not merely echo whatever
        // string this particular request happened to spell it as.
        await personaAvatarStore.UpsertAsync(
            new PersonaAvatarInput(id, item.Bytes, item.Sha256, GenerateToken(), PersonaAvatarSource.Catalog, pack.Slug),
            ct);

        logger.LogInformation(
            "Persona avatar applied from pack personaId={PersonaId} packSlug={PackSlug} item={Item}",
            id, LogSafeText.Sanitize(pack.Slug), LogSafeText.Sanitize(request.ItemName));
        return await ToDtoResultAsync(id, ct);
    }

    /// <summary>
    /// DELETE /api/personas/{id}/avatar — removes the worn face, if any (SPEC F128.6's own "DELETE
    /// removes the face" line). Deliberately NO separate persona-existence pre-check, unlike
    /// <see cref="Put"/>/<see cref="ApplyFromPack"/>: <see cref="IPersonaAvatarStore.DeleteAsync"/>'s
    /// own contract already reports "was there a row to delete" as a plain <see langword="bool"/> with
    /// no reference to guard and no write to attempt against a foreign key, so there is nothing an
    /// up-front existence check would protect here that this single call doesn't already answer more
    /// cheaply — mirrors <see cref="AvatarPackController.Uninstall"/>/<c>SpecialsController.Delete</c>'s
    /// own "one store call, map the bool to 204/404" idiom exactly. An unknown persona id and a known
    /// persona with no face both read as 404 here — the SAME "quiet, no oracle distinction beyond 404"
    /// posture <see cref="ApplyFromPack"/>'s own remarks already establish for this controller.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var deleted = await personaAvatarStore.DeleteAsync(id, ct);
        if (!deleted)
            return NotFound(NoFaceProblem(id));

        logger.LogInformation("Persona avatar removed personaId={PersonaId}", id);
        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>The object-level existence check every write action above starts with — see this
    /// class's own remarks. Returns a 404 <see cref="IActionResult"/> when <paramref name="id"/> names
    /// no persona, <see langword="null"/> when it does (the caller's own signal to continue).</summary>
    async Task<IActionResult?> FindMissingPersonaResultAsync(long id, CancellationToken ct) =>
        await personaStore.GetByIdAsync(id, ct) is null ? NotFound(UnknownPersonaProblem(id)) : null;

    /// <summary>128-bit cryptographically random hex, freshly minted for every write — see this
    /// class's own TOKEN ENTROPY remarks for why no collision handling lives here.</summary>
    static string GenerateToken() => RandomNumberGenerator.GetHexString(TokenLength, lowercase: true);

    /// <summary>
    /// Re-reads the just-written row so the response reports what was actually persisted (mirrors
    /// <c>PersonaController.Create</c>'s own "read back after write" idiom) rather than this
    /// controller's own copy of the values it handed to <see cref="IPersonaAvatarStore.UpsertAsync"/> —
    /// in particular <c>updated_at</c>, which only the store's own <c>now()</c> ever sets.
    ///
    /// <para>
    /// <b>T295-REVIEW RIDER — THE RE-READ CAN GENUINELY COME BACK EMPTY.</b> A concurrent
    /// <c>DELETE /api/personas/{id}/avatar</c> landing between this method's caller's own
    /// <see cref="IPersonaAvatarStore.UpsertAsync"/> and this re-read is a real, reachable race — not
    /// the "should never happen" shape an <see cref="UnreachableException"/> is for. This downgrades
    /// that case to the SAME honest 404 <see cref="Get"/> already reports for "no face right now",
    /// rather than crashing the request that just successfully wrote one: the write itself already
    /// happened and already logged, so the caller reporting "it's gone again" is simply true.
    /// </para>
    /// </summary>
    async Task<IActionResult> ToDtoResultAsync(long id, CancellationToken ct)
    {
        var avatar = await personaAvatarStore.GetByPersonaIdAsync(id, ct);
        if (avatar is null)
            return NotFound(NoFaceToServeProblem(id));

        var dto = new PersonaAvatarDto(avatar.PersonaId, avatar.Token, SourceText(avatar.Source), avatar.ImportedFrom, avatar.UpdatedAt);
        return Ok(dto);
    }

    static string SourceText(PersonaAvatarSource source) => source switch
    {
        PersonaAvatarSource.Upload => "upload",
        PersonaAvatarSource.Catalog => "catalog",
        _ => throw new UnreachableException($"Unhandled {nameof(PersonaAvatarSource)} case."),
    };

    static ProblemDetails UnknownPersonaProblem(long id) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No persona with id {id} exists.",
    };

    static ProblemDetails NoFaceProblem(long id) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"Persona {id} has no worn face to remove.",
    };

    /// <summary>Shared by <see cref="Get"/> (no face right now) and <see cref="ToDtoResultAsync"/> (no
    /// face any more, by the time this re-read the row it just wrote — see that method's own T295-review
    /// rider remarks). Deliberately not <see cref="NoFaceProblem"/>: that one's own wording is
    /// DELETE-specific ("to remove"), which would misdescribe a request that never asked to remove
    /// anything.</summary>
    static ProblemDetails NoFaceToServeProblem(long id) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"Persona {id} has no worn face to serve.",
    };

    static ProblemDetails BlankFromPackFieldProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = "packSlug and itemName must both be present and non-blank.",
    };

    // T295-review rider: packSlug/itemName are caller-typed request-body fields (never re-validated
    // against a slug/name shape gate before this point — unlike a route-bound slug), so both are
    // clamped through LogSafeText.Sanitize before they're echoed into a response body, the same
    // discipline CatalogInstallShell/AvatarPackController already apply to every remote-derived
    // string these two problems' own siblings interpolate.

    static ProblemDetails UnknownPackProblem(string packSlug) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No installed avatar pack with slug \"{LogSafeText.Sanitize(packSlug)}\" exists.",
    };

    static ProblemDetails UnknownItemProblem(string packSlug, string itemName) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"Avatar pack \"{LogSafeText.Sanitize(packSlug)}\" has no item named \"{LogSafeText.Sanitize(itemName)}\".",
    };

}
