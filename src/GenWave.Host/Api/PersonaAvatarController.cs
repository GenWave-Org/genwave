using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    /// (<see cref="NormalizeFailureProblem"/> — F15.7, never named beyond the honest reason text) and
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
            return BadRequest(NormalizeFailureProblem(ImageNormalizeFailureReason.TooLarge));

        if (await FindMissingPersonaResultAsync(id, ct) is { } missing)
            return missing;

        var normalized = await imageNormalizeService.NormalizeAsync(bytes, ct);
        switch (normalized)
        {
            case ImageNormalizeResult.Failure failure:
                logger.LogInformation(
                    "Persona avatar upload rejected personaId={PersonaId} reason={Reason}", id, failure.Reason);
                return BadRequest(NormalizeFailureProblem(failure.Reason));

            case ImageNormalizeResult.Success success:
                await personaAvatarStore.UpsertAsync(
                    new PersonaAvatarInput(id, success.Bytes, success.Sha256, GenerateToken(), PersonaAvatarSource.Upload, null),
                    ct);
                logger.LogInformation("Persona avatar uploaded personaId={PersonaId}", id);
                return Ok(await ToDtoAsync(id, ct));

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

        await personaAvatarStore.UpsertAsync(
            new PersonaAvatarInput(id, item.Bytes, item.Sha256, GenerateToken(), PersonaAvatarSource.Catalog, request.PackSlug),
            ct);

        logger.LogInformation(
            "Persona avatar applied from pack personaId={PersonaId} packSlug={PackSlug} item={Item}",
            id, LogSafeText.Sanitize(request.PackSlug), LogSafeText.Sanitize(request.ItemName));
        return Ok(await ToDtoAsync(id, ct));
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
    /// </summary>
    async Task<PersonaAvatarDto> ToDtoAsync(long id, CancellationToken ct)
    {
        var avatar = await personaAvatarStore.GetByPersonaIdAsync(id, ct)
            ?? throw new UnreachableException("A face just upserted for this persona must read back.");
        return new PersonaAvatarDto(avatar.PersonaId, avatar.Token, SourceText(avatar.Source), avatar.ImportedFrom, avatar.UpdatedAt);
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

    static ProblemDetails BlankFromPackFieldProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = "packSlug and itemName must both be present and non-blank.",
    };

    static ProblemDetails UnknownPackProblem(string packSlug) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No installed avatar pack with slug \"{packSlug}\" exists.",
    };

    static ProblemDetails UnknownItemProblem(string packSlug, string itemName) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"Avatar pack \"{packSlug}\" has no item named \"{itemName}\".",
    };

    /// <summary>
    /// Honest, per-<see cref="ImageNormalizeFailureReason"/> ProblemDetails (PLAN T291/T295 rider: an
    /// over-ceiling re-encoded output must never read as a "decode error", and every other reason gets
    /// its own true title rather than a shared generic one). <see cref="ImageNormalizeFailureReason.EncodeFailed"/>
    /// covers several distinct underlying causes (a missing/unusable ffmpeg binary, a genuinely corrupt
    /// input ffmpeg's own decoder refuses, AND the defensive output-byte-ceiling case,
    /// <see cref="ImageNormalizeService.MaxOutputBytes"/>'s own remarks) — none of which is a "decode"
    /// problem specifically, so its own title stays deliberately generic ("could not be processed")
    /// rather than naming a stage this reason does not uniquely pin down; F15.7 already forbids naming
    /// the exact gate/gate-internal detail in any of these bodies regardless.
    /// </summary>
    static ProblemDetails NormalizeFailureProblem(ImageNormalizeFailureReason reason) => reason switch
    {
        ImageNormalizeFailureReason.Empty => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Empty upload.",
            Detail = "The request body was empty.",
        },
        ImageNormalizeFailureReason.TooLarge => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Upload too large.",
            Detail = $"The uploaded image must be at most {ImageNormalizeService.MaxInputBytes / (1024 * 1024)} MiB.",
        },
        ImageNormalizeFailureReason.NotAnImage => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Unsupported image format.",
            Detail = "The uploaded file is not a recognized PNG or JPEG image.",
        },
        ImageNormalizeFailureReason.Animated => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Animated images are not supported.",
            Detail = "An animated PNG (APNG) cannot be used as a face.",
        },
        ImageNormalizeFailureReason.DimensionsTooSmall => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Image too small.",
            Detail = $"The uploaded image must be at least {ImageNormalizeService.MinDimensionPx}x{ImageNormalizeService.MinDimensionPx} pixels.",
        },
        ImageNormalizeFailureReason.DimensionsTooLarge => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Image dimensions too large.",
            Detail = $"The uploaded image must be at most {ImageNormalizeService.MaxDimensionPx}x{ImageNormalizeService.MaxDimensionPx} pixels.",
        },
        ImageNormalizeFailureReason.EncodeFailed => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Could not process image.",
            Detail = "The uploaded image could not be processed into a face.",
        },
        _ => throw new UnreachableException($"Unhandled {nameof(ImageNormalizeFailureReason)} case."),
    };
}
