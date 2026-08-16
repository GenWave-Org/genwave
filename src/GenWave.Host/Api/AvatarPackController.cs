using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Catalog;
using GenWave.Host.Images;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/avatar-packs/{slug}/install</c> + <c>DELETE /api/avatar-packs/{slug}</c> (SPEC F128.3,
/// STORY-332, PLAN T293) — installs/uninstalls a Dean-curated avatar pack from the Community Catalog's
/// <c>avatar</c> kind into this station's own library (<c>station.avatar_pack</c>+<c>_item</c>). Also
/// <c>GET /api/avatar-packs</c> (PLAN T294, see <see cref="List"/>'s own remarks) — the Wardrobe
/// Avatars tab's own listing route, metadata only. F79 shell, POLICY PARITY WITH FONTS (SPEC F128.3's
/// own words): mirrors
/// <see cref="FontPackController"/> almost verbatim — the same <see cref="AdminSurfaceAttribute"/> +
/// <see cref="AuthorizationPolicies.Settings"/> pairing, the same catalog-slug vocabulary
/// (<see cref="CatalogIndexValidator.SlugSegment"/>), the same "no request body, every byte fetched
/// server-side through the guarded door" posture, and the same NO-oracle <see cref="ProblemDetails"/>
/// idioms (F15.7 — no internal detail in a body).
///
/// <para>
/// <b>Gate order.</b> Route slug format (400) → catalog kill-switch (404, bare) → resolve the entry
/// (<see cref="CatalogInstallShell.ResolveEntryAsync"/>: unknown slug or a non-avatar kind ⇒ 404;
/// unreachable ⇒ 503; a withheld manifest/meta ⇒ 502) → fetch and hash-verify EVERY declared asset, one
/// at a time (<see cref="CatalogInstallShell.FetchAllAssetsAsync"/>: any withheld asset ⇒ 502, NOTHING
/// stored), the app-side pack-bytes ceiling (400, see <see cref="MaxPackBytes"/>'s own remarks) checked
/// against the RUNNING total after EACH one — the SAME early-cutoff discipline
/// <see cref="FontPackController"/>'s own N1 review finding established, mirrored here from the start
/// rather than re-discovered → parse the manifest (<see cref="CatalogAvatarPackManifestSerializer.Deserialize"/>:
/// reject ⇒ 400) → the app-side ITEM-COUNT ceiling (400, see <see cref="MaxPackItems"/>'s own remarks,
/// review finding S1) → cross-check the manifest's own <c>items[]</c> against what was actually
/// fetched: each item's own <c>name</c> gets a shape gate (400, see <see cref="IsValidItemName"/>'s own
/// remarks, review finding S2) BEFORE the DUPLICATE-name check within <c>items[]</c> itself, before
/// either ever reaches the store (400 — <c>station.avatar_pack_item</c>'s own <c>UNIQUE(pack_id, name)</c>
/// constraint, db/37, is the rule the duplicate check pre-checks; UNLIKE <see cref="FontPackController"/>'s
/// own duplicate-FILE check, a duplicate FILE across two DIFFERENTLY-NAMED items is not a store-level
/// collision here at all — two items legitimately sharing one image is not this route's business to
/// forbid), each item's own OPTIONAL <c>suggestedPersona</c> degrading to <see langword="null"/> on a
/// bad shape rather than rejecting the install (review finding S2, see
/// <see cref="CatalogInstallShell.ValidateSuggestedPersonaShape"/>'s own remarks) → re-validate and
/// normalize EVERY fetched item's bytes, memoized per distinct FILE so two items sharing one already-
/// fetched image re-encode exactly once (<see cref="ImageNormalizeService.NormalizeAsync"/>: ANY
/// failure fails the WHOLE install, see this class's own RE-VALIDATION remarks and
/// <see cref="NormalizeAllItemsAsync"/>'s own MEMOIZATION remarks, review finding S1) → ONE
/// <see cref="IAvatarPackStore.UpsertAsync"/> call → 200.
/// </para>
///
/// <para>
/// <b>RE-VALIDATION IS NOT OPTIONAL (SPEC F128.3's own words: "the catalog's CI is never trusted";
/// SPEC F129.2's "served bytes are metadata-free by construction", PLAN T293 orchestrator addition).</b>
/// A CI-approved index entry proves only that catalog CI, at PUBLISH time, once, looked at these
/// bytes — it says nothing about what THIS install actually fetched (a stale/compromised/hand-edited
/// index this station's own transport would otherwise trust without complaint, the exact threat model
/// <see cref="FontPackController.MaxPackBytes"/>'s own remarks already name for a font pack). A PNG
/// that hash-verifies cleanly against the index's own declared sha256 can still carry an <c>acTL</c>
/// (APNG) chunk, or tEXt/eXIf metadata that would otherwise ride straight through to a publicly-served
/// face — hash verification proves the bytes are what the INDEX says; it proves nothing about what
/// those bytes actually ARE. <see cref="NormalizeAllItemsAsync"/> runs EVERY fetched item through the
/// SAME <see cref="ImageNormalizeService"/> gates a direct owner upload passes (magic bytes → header
/// dimensions/APNG-reject → ffmpeg center-crop-and-scale re-encode) — the re-encode is what
/// structurally strips any metadata chunk, never a chunk-by-chunk filter this route would have to keep
/// in lockstep with the pipeline's own gates. A single item failing ANY gate fails the WHOLE install
/// (SPEC F104 "a pack IS its files" all-or-nothing posture, applied here identically): nothing is ever
/// written for a partially-good pack, and the refusal is a quiet 400 naming no gate/reason (F15.7 — an
/// admin-only surface is still no reason to hand a caller a validation oracle for what shape of hostile
/// PNG a catalog origin could smuggle past this route).
/// </para>
///
/// <para>
/// <b>STORED BYTES ARE THE NORMALIZED DERIVATIVE, STORED HASH IS RECOMPUTED OVER THEM — a deliberate
/// divergence from <see cref="FontPackController"/>'s own "the store persists whatever the transport
/// already verified" idiom.</b> A font face is stored VERBATIM (bytes = what the transport fetched,
/// sha256 = the index's own pinned hash) because nothing further happens to it. An avatar item is
/// NOT: <see cref="ImageNormalizeService.NormalizeAsync"/> replaces the fetched bytes with a freshly
/// re-encoded 512×512 PNG before this route ever constructs an <see cref="AvatarPackItemInput"/>, so
/// <c>station.avatar_pack_item.bytes</c>/<c>.sha256</c> describe THAT derivative, never the bytes the
/// index's own sha256 pinned. The FETCH's own integrity is still fully verified — <see cref="CatalogProxyService.GetAssetAsync"/>
/// hash-checks every byte against the index before this route ever sees it, exactly as
/// <see cref="FontPackController"/>'s own fetch does — that check is just never carried any further:
/// once normalization replaces the payload, continuing to store the FETCH's own hash next to
/// DIFFERENT bytes would be actively dishonest (a stored hash that does not describe the stored
/// payload), so <see cref="ImageNormalizeService.NormalizeAsync"/>'s own freshly-computed
/// <see cref="ImageNormalizeResult.Success.Sha256"/> is what <see cref="AvatarPackItemInput.Sha256"/>
/// actually carries — see that type's own remarks for the same reasoning, recorded once more at its
/// own home.
/// </para>
///
/// <para>
/// <b>UNINSTALL IS GUARD-FREE, DELIBERATELY (SPEC F128.3/F128.5, ARCHITECTURE.md's "assignment
/// copies, provenance records" ruling) — a deliberate divergence from <see cref="FontPackController.Uninstall"/>'s
/// own referenced-by 409 guard.</b> A worn face already applied to a persona
/// (<c>station.persona_avatar</c>, PLAN T295's write path) is a COPY of a pack item's bytes at the
/// moment it was applied, never a live reference into <c>station.avatar_pack_item</c> — the exact
/// opposite of a saved theme's own live reference into <c>station.font_pack_face</c>, which is WHY
/// that sibling route needs a guard and this one does not. <see cref="IAvatarPackStore.DeleteAsync"/>'s
/// own remarks make the same point at the seam this route calls. Removing a pack can therefore never
/// blank a DJ's face mid-broadcast — <see cref="Uninstall"/> simply deletes and reports 404/204, no
/// referenced-by check to race past, no third response shape to map.
/// </para>
/// </summary>
[ApiController]
[Route("api/avatar-packs")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class AvatarPackController(
    CatalogProxyService catalogProxyService,
    CommunityCatalogAccessor catalogAccessor,
    IAvatarPackStore avatarPackStore,
    ImageNormalizeService imageNormalizeService,
    ILogger<AvatarPackController> logger) : ControllerBase
{
    /// <summary>
    /// The app-side backstop over what this route actually FETCHES (mirrors
    /// <see cref="FontPackController.MaxPackBytes"/>'s own remarks, re-derived for the avatar kind's
    /// own catalog-CI ceiling instead of the font one). The REAL ≤6 MiB per-pack ceiling is enforced
    /// upstream, once, at catalog CI publish time (SPEC F128.1) — this constant is this app's OWN
    /// re-assertion of the identical magnitude as defense-in-depth against a
    /// stale/compromised/hand-edited index this station's transport would otherwise fetch and store
    /// without complaint. Summed over every declared asset <see cref="CatalogInstallShell.FetchAllAssetsAsync"/>
    /// fetches, checked against the RUNNING total INSIDE that method's own fetch loop, not after it —
    /// the same early-cutoff discipline <see cref="FontPackController"/>'s own N1 review finding
    /// established (mirrored here from the start: a hand-edited index naming far more than 6 MiB of
    /// assets is refused the moment the total crosses it, never buffered in full first).
    /// </summary>
    public const long MaxPackBytes = 6 * 1024 * 1024;

    /// <summary>
    /// The app-side ceiling on <c>manifest.Items.Count</c> (review finding S1) — bounds the peak
    /// memory <see cref="NormalizeAllItemsAsync"/> can ever hold BEFORE the single
    /// <see cref="IAvatarPackStore.UpsertAsync"/> call, independent of <see cref="MaxPackBytes"/>: that
    /// ceiling only bounds the FETCH stage's own running total, but <see cref="BuildRawItems"/>'s own
    /// remarks explain why a manifest may legitimately declare many items sharing one already-fetched
    /// file — an unbounded item COUNT could still drive the normalize stage to hold up to (item count)
    /// × <see cref="ImageNormalizeService.MaxOutputBytes"/> (512 KiB) of normalized output at once
    /// (<see cref="NormalizeAllItemsAsync"/>'s own per-file memoization caps the number of ffmpeg
    /// invocations, never the number of ITEMS this loop still has to hold a normalized copy for —
    /// every item, memoized or not, still gets its own <see cref="AvatarPackItemInput"/> in the final
    /// list). ~8,700 items × 512 KiB ≈ 4.2 GiB — enough to OOM this process on a 4 GB box (dead air).
    /// SPEC F128.1's own catalog-CI arithmetic (≤512 KiB per item, ≤6 MiB per pack) already caps a
    /// CI-legal pack at 12 items, and the seed packs (SPEC F128.10) ship exactly 12; 64 is generous
    /// headroom over that CI-legal maximum, not a number this app expects a legitimate pack to
    /// approach.
    /// </summary>
    public const int MaxPackItems = 64;

    /// <summary>An item's own display name (SPEC F128.1) is a manifest field, not a slug — bounded to a
    /// sane display-string length rather than <see cref="CatalogInstallShell.MaxSlugLength"/>'s own
    /// slug-shaped cap (review finding S2); see <see cref="IsValidItemName"/>'s own remarks for the
    /// full shape gate.</summary>
    const int MaxItemNameLength = 64;

    /// <summary>
    /// POST /api/avatar-packs/{slug}/install — see this class's own remarks for the full gate order
    /// and the reasoning behind each one.
    /// </summary>
    [HttpPost("{slug}/install")]
    public async Task<IActionResult> Install(string slug, CancellationToken ct)
    {
        if (slug.Length > CatalogInstallShell.MaxSlugLength)
            return BadRequest(CatalogInstallShell.SlugTooLongProblem(slug.Length));

        if (!CatalogInstallShell.SlugFormat().IsMatch(slug))
            return BadRequest(CatalogInstallShell.BadSlugProblem(slug));

        if (!catalogAccessor.IsEnabled)
            return CatalogInstallShell.DisabledSurfaceResult(Response);

        var (entryError, entryContent) = await CatalogInstallShell.ResolveEntryAsync(
            catalogProxyService, CatalogEntryKind.Avatar, slug, ct);
        if (entryError is not null)
            return entryError;
        if (entryContent is not { } content)
            throw new UnreachableException("CatalogInstallShell.ResolveEntryAsync returned neither an error nor content.");

        var (assetsError, fetchedAssetsOrNull) = await CatalogInstallShell.FetchAllAssetsAsync(
            catalogProxyService, slug, content,
            new CatalogInstallShell.PackFetchPolicy(CatalogEntryKind.Avatar, CatalogIndexValidator.MaxPngAssetBytes, MaxPackBytes, "SPEC F128.1"),
            ct);
        if (assetsError is not null)
            return assetsError;
        if (fetchedAssetsOrNull is not { } fetchedAssets)
            throw new UnreachableException("CatalogInstallShell.FetchAllAssetsAsync returned neither an error nor a fetched-asset map.");

        var manifest = CatalogAvatarPackManifestSerializer.Deserialize(content.ManifestJson);
        if (manifest is null)
            return BadRequest(CatalogInstallShell.MalformedManifestProblem(CatalogEntryKind.Avatar, slug));

        var (rawError, rawItemsOrNull) = BuildRawItems(slug, manifest, fetchedAssets);
        if (rawError is not null)
            return rawError;
        if (rawItemsOrNull is not { } rawItems)
            throw new UnreachableException("BuildRawItems returned neither an error nor an item list.");

        var (normalizeError, itemsOrNull) = await NormalizeAllItemsAsync(slug, rawItems, ct);
        if (normalizeError is not null)
            return normalizeError;
        if (itemsOrNull is not { } items)
            throw new UnreachableException("NormalizeAllItemsAsync returned neither an error nor an item list.");

        await avatarPackStore.UpsertAsync(slug, content.ManifestJson, slug, items, ct);

        logger.LogInformation(
            "Avatar pack installed slug={Slug} packName={PackName} itemCount={ItemCount}",
            LogSafeText.Sanitize(slug), LogSafeText.Sanitize(manifest.PackName), items.Count);

        return Ok(new AvatarPackInstallResponse(slug, manifest.PackName, items.Select(i => i.Name).ToArray(), slug));
    }

    // ── Uninstall (DELETE /api/avatar-packs/{slug}) ─────────────────────────

    /// <summary>
    /// DELETE /api/avatar-packs/{slug} — uninstalls a pack (SPEC F128.3, STORY-332, PLAN T293). See
    /// this class's own UNINSTALL IS GUARD-FREE remarks for why this carries no referenced-by check,
    /// unlike <see cref="FontPackController.Uninstall"/>'s own 409 guard. With the pack found, 204: the
    /// pack row and every one of its items are gone (db/37's own <c>ON DELETE CASCADE</c>). Loud either
    /// way (mirrors <see cref="FontPackController.Uninstall"/>'s own logging posture): INFO on an
    /// actual delete naming the slug, a genuinely unknown slug is a plain 404 with nothing to log.
    /// </summary>
    [HttpDelete("{slug}")]
    public async Task<IActionResult> Uninstall(string slug, CancellationToken ct)
    {
        if (slug.Length > CatalogInstallShell.MaxSlugLength)
            return BadRequest(CatalogInstallShell.SlugTooLongProblem(slug.Length));

        if (!CatalogInstallShell.SlugFormat().IsMatch(slug))
            return BadRequest(CatalogInstallShell.BadSlugProblem(slug));

        var deleted = await avatarPackStore.DeleteAsync(slug, ct);
        if (!deleted)
            return NotFound(CatalogInstallShell.UnknownInstalledPackProblem(CatalogEntryKind.Avatar, slug));

        logger.LogInformation("Avatar pack uninstalled slug={Slug}", LogSafeText.Sanitize(slug));
        return NoContent();
    }

    // ── Library listing (GET /api/avatar-packs) ─────────────────────────────

    /// <summary>
    /// GET /api/avatar-packs — every installed pack (SPEC F128.3, STORY-332, PLAN T294): the pack's
    /// own manifest name (re-parsed from the stored <c>definition</c>, degrading to
    /// <see langword="null"/> on the should-never-happen re-parse failure — mirrors
    /// <see cref="FontPackController.List"/>'s own <c>ToLibraryDto</c> posture), each item's own
    /// name+suggestion (NO bytes — the Wardrobe Avatars tab's own face grid reads bytes through the
    /// TRANSIENT proxied catalog route instead, the F104 specimen precedent, never this listing), and
    /// imported_from/imported_at provenance (db/25 pattern). Reads
    /// <see cref="IAvatarPackStore.GetAllAsync"/> ONCE for every pack row WITH its own item
    /// name/suggestion metadata already folded in (review finding B1 — mirrors
    /// <see cref="FontPackController.List"/>'s own single-<c>GetAllAsync</c>-call shape exactly; this
    /// route used to call <see cref="IAvatarPackStore.GetBySlugAsync"/> a second time PER PACK just to
    /// read that same metadata off a bytes-carrying read, discarding up to 6 MiB of item payload per
    /// pack per request for nothing this listing ever used). Ordered by slug (ordinal) for a stable,
    /// deterministic listing — mirrors <see cref="FontPackController.List"/>'s own rule.
    ///
    /// <para>
    /// <b>NO <see cref="CommunityCatalogAccessor.IsEnabled"/> GATE — DELIBERATE</b> (mirrors
    /// <see cref="FontPackController.List"/>'s own remarks verbatim, applied to the avatar kind): this
    /// action lists what is ALREADY INSTALLED — <c>station.avatar_pack</c>(+<c>_item</c>) rows this
    /// station wrote for itself at some past install, station-local state that outlives the catalog
    /// the same way an installed font pack does. The kill switch gates DISCOVERY of new packs
    /// (<see cref="Install"/>), never REMEMBRANCE of installed ones.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var packs = await avatarPackStore.GetAllAsync(ct);
        return Ok(packs.OrderBy(p => p.Slug, StringComparer.Ordinal).Select(ToSummaryDto).ToArray());
    }

    /// <summary>See <see cref="AvatarPackSummaryDto"/>'s own remarks for why <see cref="AvatarPackSummaryDto.Name"/>
    /// is the only field this re-parse can affect.</summary>
    static AvatarPackSummaryDto ToSummaryDto(AvatarPackSummary pack)
    {
        var manifest = CatalogAvatarPackManifestSerializer.Deserialize(pack.Definition);
        return new AvatarPackSummaryDto(
            pack.Slug,
            manifest?.PackName,
            pack.Items.Select(item => new AvatarPackSummaryItemDto(item.Name, item.SuggestedPersona)).ToArray(),
            pack.ImportedFrom,
            pack.ImportedAt);
    }

    // ── Entry resolution + asset fetch (SHARED SHELL, PLAN T293 review finding S6) ──────────────────
    //
    // Both moved to CatalogInstallShell.ResolveEntryAsync/FetchAllAssetsAsync once this controller
    // became a second near-verbatim copy of FontPackController's own shape — see that type's own
    // remarks for the full reasoning, unchanged in substance from when each lived here. Install (above)
    // is the one caller of both.

    // ── Manifest cross-check ─────────────────────────────────────────────────

    /// <summary>
    /// Cross-checks the parsed manifest's own <c>items[]</c> against what was actually fetched, and its
    /// own item COUNT against <see cref="MaxPackItems"/> (400, review finding S1 — checked FIRST, before
    /// this method allocates or iterates anything proportional to a hostile item count). Each item then
    /// runs through two shape gates BEFORE the duplicate-name check that follows them (review finding
    /// S2): <see cref="IsValidItemName"/> on <c>name</c> (400, reject — a malformed name is not this
    /// route's to silently accept), and <see cref="CatalogInstallShell.ValidateSuggestedPersonaShape"/>
    /// on the OPTIONAL <c>suggestedPersona</c> hint (degrade to <see langword="null"/>, never reject — a
    /// suggestion is an OFFER, SPEC F128.5, not a value this route depends on). The duplicate-name check
    /// itself (the SAME "manifest ⊆ fetched assets" guard <see cref="FontPackController.BuildFaces"/>
    /// already applies to a font pack's own <c>files[]</c>) rejects a DUPLICATE item <c>name</c> within
    /// <c>items[]</c> BEFORE a single byte reaches <see cref="ImageNormalizeService"/> or the store —
    /// <c>station.avatar_pack_item.(pack_id, name)</c> is UNIQUE (db/37), the real constraint a manifest
    /// naming the same item twice would otherwise die against as a real Postgres 23505 with a misleading
    /// generic detail, mirroring the reasoning behind <see cref="FontPackController"/>'s own N2 review
    /// finding. UNLIKE that sibling check, this dedupes by NAME, not by <c>file</c> — an avatar pack's
    /// uniqueness key is scoped per-pack by name (db/37), not globally by filename the way
    /// <c>font_pack_face.file</c> is, so two DIFFERENTLY-NAMED items legitimately sharing one PNG (e.g.
    /// two personas offered the identical stock face) is not a collision this route forbids —
    /// <see cref="NormalizeAllItemsAsync"/>'s own memoization is what makes that shared-image case cost
    /// exactly one re-encode, not two.
    /// </summary>
    (IActionResult? Error, IReadOnlyList<RawItem>? Items) BuildRawItems(
        string slug, CatalogAvatarPackManifest manifest, Dictionary<string, CatalogInstallShell.CatalogFetchedAsset> fetchedAssets)
    {
        if (manifest.Items.Count > MaxPackItems)
            return (BadRequest(TooManyManifestItemsProblem(slug, manifest.Items.Count)), null);

        var items = new List<RawItem>(manifest.Items.Count);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in manifest.Items)
        {
            if (!IsValidItemName(item.Name))
                return (BadRequest(InvalidItemNameProblem(slug)), null);

            if (!seenNames.Add(item.Name))
                return (BadRequest(DuplicateManifestItemNameProblem(slug, item.Name)), null);

            if (!fetchedAssets.TryGetValue(item.File, out var asset))
                return (BadRequest(CatalogInstallShell.UndeclaredManifestAssetProblem(CatalogEntryKind.Avatar, slug, item.File)), null);

            var suggestedPersona = CatalogInstallShell.ValidateSuggestedPersonaShape(item.SuggestedPersona);
            items.Add(new RawItem(item.Name, item.File, asset.Bytes, suggestedPersona));
        }

        return (null, items);
    }

    /// <summary>
    /// A display name's own shape gate (review finding S2) — an item <c>name</c> is a MANIFEST field,
    /// never a slug (unlike the route <c>slug</c> itself, <see cref="CatalogInstallShell.SlugFormat"/>'s
    /// own vocabulary does not apply here): bounded to <see cref="MaxItemNameLength"/> characters and
    /// free of control characters (a printable-only check, the same "sane display string" bar
    /// <c>LogSafeText.Sanitize</c> polices for a LOGGED string, applied here at the GATE instead of
    /// after the fact) — an empty name is rejected too, the same "never a blank label" floor a display
    /// string is always held to.
    /// </summary>
    static bool IsValidItemName(string name) =>
        name.Length is > 0 and <= MaxItemNameLength && !name.Any(char.IsControl);

    /// <summary>One manifest item's own name/file/fetched-bytes/shape-checked suggestion, cross-checked
    /// but not yet re-validated or normalized — <see cref="NormalizeAllItemsAsync"/>'s own input shape.
    /// <see cref="File"/> is carried alongside <see cref="Bytes"/> (review finding S1) as that method's
    /// own memoization key — two items sharing one <see cref="File"/> share the identical fetched
    /// <see cref="Bytes"/> reference too (both resolved from the SAME <c>fetchedAssets</c> entry in
    /// <see cref="BuildRawItems"/>), so keying by either would work; <see cref="File"/> is the more
    /// honest key, since it is what "the same underlying asset" actually MEANS here, not an incidental
    /// consequence of how the dictionary happens to be built.</summary>
    sealed record RawItem(string Name, string File, byte[] Bytes, string? SuggestedPersona);

    // ── Re-validation + normalize (SPEC F128.3/F129.2, PLAN T293 orchestrator addition) ───────────

    /// <summary>
    /// Runs EVERY <paramref name="rawItems"/> entry through <see cref="ImageNormalizeService.NormalizeAsync"/>
    /// — see this class's own RE-VALIDATION IS NOT OPTIONAL and STORED BYTES ARE THE NORMALIZED
    /// DERIVATIVE remarks for why this exists and what it stores. A SINGLE failing item fails the
    /// WHOLE install (F104 "a pack IS its files" all-or-nothing posture): the instant one item fails
    /// any gate, this returns immediately with nothing built for any LATER item in the list, and
    /// nothing this call already normalized is ever handed to <see cref="IAvatarPackStore.UpsertAsync"/>
    /// (the caller only ever reaches that call with THIS method's own success list, never a partial
    /// one). The exact <see cref="ImageNormalizeFailureReason"/> is WARN-logged (server-side,
    /// sanitized) but never reaches <paramref name="slug"/>'s own caller — F15.7's "no internal detail
    /// in a body" posture: a hostile catalog origin gets no oracle for which gate its next attempt
    /// should try to slip past.
    ///
    /// <para>
    /// <b>MEMOIZATION, PER DISTINCT FILE (review finding S1).</b> <see cref="BuildRawItems"/>'s own
    /// remarks establish that two items may legitimately share one already-fetched image (a DIFFERENT
    /// item NAME pointing at the SAME manifest <c>file</c>) — without this cache, each such item would
    /// re-run the full ffmpeg re-encode independently, turning an item-count multiplier into a
    /// process-spawn multiplier too. <paramref name="rawItems"/>'s own <see cref="RawItem.File"/> is
    /// the cache key: the FIRST item naming a given file pays for the real
    /// <see cref="ImageNormalizeService.NormalizeAsync"/> call (and, on failure, still fails the whole
    /// install exactly as an un-memoized call would — nothing here changes WHICH items can fail, only
    /// how many times an already-decided outcome is computed); every LATER item naming that same file
    /// reuses the cached <see cref="ImageNormalizeResult.Success"/> outright. Only successes are ever
    /// cached — a failure always ends this whole method on the spot (see this method's own ALL-OR-
    /// NOTHING remarks above), so there is never a cached FAILURE a later item could reuse instead of
    /// re-running the gate.
    /// </para>
    /// </summary>
    async Task<(IActionResult? Error, IReadOnlyList<AvatarPackItemInput>? Items)> NormalizeAllItemsAsync(
        string slug, IReadOnlyList<RawItem> rawItems, CancellationToken ct)
    {
        var normalized = new List<AvatarPackItemInput>(rawItems.Count);
        var normalizedByFile = new Dictionary<string, ImageNormalizeResult.Success>(StringComparer.Ordinal);

        foreach (var raw in rawItems)
        {
            if (!normalizedByFile.TryGetValue(raw.File, out var success))
            {
                var result = await imageNormalizeService.NormalizeAsync(raw.Bytes, ct);
                switch (result)
                {
                    case ImageNormalizeResult.Success ok:
                        success = ok;
                        normalizedByFile[raw.File] = ok;
                        break;
                    case ImageNormalizeResult.Failure failure:
                        logger.LogWarning(
                            "Avatar pack item failed server-side re-validation slug={Slug} item={Item} reason={Reason}",
                            LogSafeText.Sanitize(slug), LogSafeText.Sanitize(raw.Name), failure.Reason);
                        return (BadRequest(ItemFailedRevalidationProblem(slug)), null);
                    default:
                        throw new UnreachableException($"Unhandled {nameof(ImageNormalizeResult)} case.");
                }
            }

            normalized.Add(new AvatarPackItemInput(raw.Name, success.Bytes, success.Sha256, raw.SuggestedPersona));
        }

        return (null, normalized);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    //
    // The slug-format regex/cap, DisabledSurfaceResult, and the generic unknown-pack/catalog-
    // unavailable/withheld/pack-too-large/malformed-manifest/undeclared-asset ProblemDetails factories
    // all moved to CatalogInstallShell (PLAN T293 review finding S6) once this controller became a
    // second byte-identical copy of every one of them — see that type's own remarks. What remains here
    // is AVATAR-SPECIFIC: the item-count ceiling, the item-name/duplicate-name checks (an avatar pack
    // dedupes by NAME, never by FILE — no font-pack-shaped counterpart), and the re-validation refusal.

    static ProblemDetails TooManyManifestItemsProblem(string slug, int count) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Avatar pack has too many items.",
        Detail = $"\"{slug}\" declares {count} items, over the {MaxPackItems}-item pack ceiling.",
    };

    // Deliberately no item-name detail here (review finding S2) — a name failing THIS gate is exactly
    // the shape LogSafeText.Sanitize exists to keep out of a body unbounded/unstripped; the quiet-400
    // posture mirrors ItemFailedRevalidationProblem's own "no oracle" reasoning below.
    static ProblemDetails InvalidItemNameProblem(string slug) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Malformed avatar pack manifest.",
        Detail = $"\"{slug}\"'s manifest names an item outside the allowed shape (printable, 1–{MaxItemNameLength} characters).",
    };

    // name is a manifest field off an untrusted, remote origin — sanitized (review finding S2) rather
    // than interpolated raw, the same discipline CatalogInstallShell.UndeclaredManifestAssetProblem
    // now applies to its own remote file parameter.
    static ProblemDetails DuplicateManifestItemNameProblem(string slug, string name) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Malformed avatar pack manifest.",
        Detail = $"\"{slug}\"'s manifest lists an item named \"{LogSafeText.Sanitize(name)}\" more than once in items[].",
    };

    // Deliberately no gate/reason detail here (F15.7 — this class's own RE-VALIDATION remarks): the
    // exact ImageNormalizeFailureReason is already in the WARN this route logs server-side.
    static ProblemDetails ItemFailedRevalidationProblem(string slug) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Avatar pack failed re-validation.",
        Detail = $"\"{slug}\" contains an item that failed server-side image validation and was withheld — nothing was installed.",
    };
}
