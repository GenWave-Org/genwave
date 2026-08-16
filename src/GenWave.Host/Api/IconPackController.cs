using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Catalog;
using GenWave.Host.Icons;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/icon-packs/{slug}/install</c> + <c>DELETE /api/icon-packs/{slug}</c> (SPEC F130.5,
/// STORY-337, PLAN T303) — installs/uninstalls a Dean-curated icon pack from the Community Catalog's
/// <c>icon</c> kind into this station's own library (<c>station.icon_pack</c>). Also
/// <c>GET /api/icon-packs</c> (listing, this file's own <see cref="List"/>) and
/// <c>GET /api/icon-packs/active</c> (the currently activated pack's own definition, this file's own
/// <see cref="Active"/> — the smallest honest surface a future renderer, PLAN T304, needs client-side).
/// F79 shell, mirrors <see cref="AvatarPackController"/> almost verbatim: the same
/// <see cref="AdminSurfaceAttribute"/> + <see cref="AuthorizationPolicies.Settings"/> pairing, the same
/// catalog-slug vocabulary (<see cref="CatalogIndexValidator.SlugSegment"/>), the same "no request body,
/// every byte fetched server-side through the guarded door" posture, and the same NO-oracle
/// <see cref="ProblemDetails"/> idioms (F15.7 — no internal detail in a body).
///
/// <para>
/// <b>NO ASSETS[] TO FETCH — the whole reason this controller is SHORTER than its avatar/font siblings
/// (SPEC F130.6).</b> An icon entry carries no binary <c>assets[]</c> at all — the constrained-vector
/// document IS the manifest file, already fetched, hash-verified, AND size-capped DURING that one
/// streamed read by <see cref="CatalogInstallShell.ResolveEntryAsync"/>'s own call into
/// <see cref="CatalogProxyService.GetEntryAsync"/> (<see cref="CatalogProxyService.MaxCardBytes"/>, 256
/// KiB — the EXACT same magnitude as <see cref="IconPackDefinitionParser.MaxDefinitionBytes"/>, pinned
/// equal by <c>Story337_IconPacksSwapTheChrome.cs</c>'s own
/// <c>InstallCapEqualsTheDefinitionParsersOwnCap</c> fact, PLAN T303 review rider 3 — never a
/// coincidence two independently-chosen ceilings happen to share today). Unlike
/// <see cref="AvatarPackController.Install"/>/<see cref="FontPackController.Install"/>, this route never
/// calls <see cref="CatalogInstallShell.FetchAllAssetsAsync"/> — there is nothing further to fetch (PLAN
/// T303's own rider 6: the fetch-cap rider is N/A for icon entries, recorded here rather than silently
/// dropped).
/// </para>
///
/// <para>
/// <b>Gate order.</b> Route slug format (400) → catalog kill-switch (404, bare) → resolve the entry
/// (<see cref="CatalogInstallShell.ResolveEntryAsync"/>: unknown slug or a non-icon kind ⇒ 404;
/// unreachable ⇒ 503; a withheld manifest/meta ⇒ 502) → <see cref="IconPackDefinitionParser.Validate"/>
/// against the fetched manifest bytes (PLAN T302's own whitelist/grammar/bound gates — a quiet 400 on
/// <see cref="IconPackValidationResult.Invalid"/>, the real reason WARN-logged only, never echoed into
/// the response body — F15.7, this class's own RE-VALIDATION IS THE WHOLE GATE remarks) → the ONE
/// install-time WARN for any out-of-contract <see cref="IconPackValidationResult.Valid.IgnoredNames"/>
/// (SPEC F130.2) → <see cref="IconPackDefinitionSerializer.Serialize"/> the validated MODEL (never the
/// raw fetched bytes — PLAN T303 review rider 2, see that type's own remarks) → ONE
/// <see cref="IIconPackStore.UpsertAsync"/> call → 200.
/// </para>
///
/// <para>
/// <b>RE-VALIDATION IS THE WHOLE GATE (unlike <see cref="AvatarPackController"/>'s own two-stage
/// "catalog CI, then a SECOND server-side re-validation/re-encode" story).</b> An avatar/font pack's own
/// binary payload needs a structural re-encode (PNG normalize, or nothing further for a font face) on
/// top of the index's own hash verification, because the byte-level threat model (embedded metadata
/// chunks, a hostile magic-byte lie) cannot be ruled out by a text-shape check alone. An icon pack's own
/// payload is different in kind: SPEC F130.1's whitelist — a closed geometry-primitive tag set, numeric-
/// only attributes, a character-grammar-gated <c>d</c>/<c>points</c> string, fill/stroke restricted to
/// two tokens — makes <see cref="IconPackDefinitionParser.Validate"/> ITSELF the complete re-validation:
/// nothing this schema can express is capable of carrying script, a URL, CSS, or a literal color (that
/// type's own class remarks: "structurally unrepresentable… not merely rejected at the edge"), so there
/// is no further byte-level gate for this route to run on top of it. <see cref="CatalogProxyService.GetEntryAsync"/>'s
/// own hash verification already proves the fetched bytes are what the index declared; <c>Validate</c>
/// is what proves those bytes are SAFE, and re-serializing its own output (never the original bytes) is
/// what makes the STORED form canonical — see <see cref="IconPackDefinitionSerializer"/>'s own remarks
/// for the validator/renderer parser-differential this closes.
/// </para>
///
/// <para>
/// <b>UNINSTALL IS GUARD-FREE, DELIBERATELY (SPEC F130.5 — mirrors <see cref="AvatarPackController.Uninstall"/>'s
/// own remarks almost verbatim).</b> <c>Station:IconPack</c> names an installed pack by SLUG in the
/// settings overlay, never a structural foreign key into <c>station.icon_pack</c> (<see cref="IIconPackStore.DeleteAsync"/>'s
/// own remarks make the same point at the seam this route calls) — uninstalling the ACTIVE pack is
/// therefore legal and NEVER touches <c>station.settings</c> from this DELETE (F130.5's own words: "no
/// cross-store write from a DELETE"). <see cref="Active"/> is the read-side half of that same contract:
/// it resolves a dangling <c>Station:IconPack</c> value to <see cref="NoContentResult"/> (house icons),
/// never an error — the settings page's own inline notice for THAT dangling state is PLAN T304's own UI
/// concern, not this route's.
/// </para>
/// </summary>
[ApiController]
[Route("api/icon-packs")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class IconPackController(
    CatalogProxyService catalogProxyService,
    CommunityCatalogAccessor catalogAccessor,
    IIconPackStore iconPackStore,
    IOptionsMonitor<StationOptions> stationMonitor,
    ILogger<IconPackController> logger) : ControllerBase
{
    /// <summary>
    /// POST /api/icon-packs/{slug}/install — see this class's own remarks for the full gate order and
    /// the reasoning behind each one.
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
            catalogProxyService, CatalogEntryKind.Icon, slug, ct);
        if (entryError is not null)
            return entryError;
        if (entryContent is not { } content)
            throw new UnreachableException("CatalogInstallShell.ResolveEntryAsync returned neither an error nor content.");

        // content.ManifestJson is the whole icon-pack document (SPEC F130.6 — no assets[] to fetch;
        // see this class's own NO ASSETS[] TO FETCH remarks) — already hash-verified and size-capped
        // during the read above; re-encoding the parsed string back to UTF-8 bytes here is what
        // Validate's own raw-byte-length cap needs to check, not a second unbounded read.
        var definitionBytes = Encoding.UTF8.GetBytes(content.ManifestJson);
        switch (IconPackDefinitionParser.Validate(definitionBytes))
        {
            case IconPackValidationResult.Valid valid:
                if (valid.IgnoredNames.Count > 0)
                {
                    // SPEC F130.2's own "ignored with ONE install-time WARN" — every name sanitized
                    // AND length-clamped (PLAN T303 review rider 4: LogSafeText.Sanitize does both in
                    // one call, its own remarks) even though every name here has already passed
                    // IconPackDefinitionParser's own shape/length gate — belt-and-suspenders, the same
                    // "every string in a log line goes through Sanitize" rule this codebase already
                    // holds every other catalog log line to.
                    logger.LogWarning(
                        "Icon pack install ignored names outside the contract slug={Slug} names={Names}",
                        LogSafeText.Sanitize(slug),
                        string.Join(", ", valid.IgnoredNames.Select(LogSafeText.Sanitize)));
                }

                // The re-serialized VALIDATED MODEL, never content.ManifestJson's own raw fetched
                // bytes (PLAN T303 review rider 2) — see IconPackDefinitionSerializer's own remarks
                // for the validator/renderer parser-differential this closes.
                var canonicalJson = IconPackDefinitionSerializer.Serialize(valid.Definition);
                await iconPackStore.UpsertAsync(slug, canonicalJson, slug, ct);

                logger.LogInformation(
                    "Icon pack installed slug={Slug} iconCount={IconCount}",
                    LogSafeText.Sanitize(slug), valid.Definition.Icons.Count);

                return Ok(new IconPackInstallResponse(slug, valid.Definition.Icons.Count, slug));

            case IconPackValidationResult.Invalid invalid:
                // The real reason is WARN-logged, sanitized AND length-clamped (PLAN T303 review
                // rider 4 — a hostile tag/attr/fill VALUE is unbounded remote text Validate's own
                // Reason may embed verbatim, see that type's own remarks), but NEVER echoed into the
                // response body (F15.7's "no internal detail in a body" posture, mirrors
                // AvatarPackController.ItemFailedRevalidationProblem's own quiet-400 shape) — a
                // hostile catalog origin gets no oracle for which gate its next attempt should try to
                // slip past.
                logger.LogWarning(
                    "Icon pack install failed validation slug={Slug} reason={Reason}",
                    LogSafeText.Sanitize(slug), LogSafeText.Sanitize(invalid.Reason));
                return BadRequest(CatalogInstallShell.MalformedManifestProblem(CatalogEntryKind.Icon, slug));

            default:
                throw new UnreachableException($"Unhandled {nameof(IconPackValidationResult)} case.");
        }
    }

    // ── Uninstall (DELETE /api/icon-packs/{slug}) ───────────────────────────

    /// <summary>
    /// DELETE /api/icon-packs/{slug} — uninstalls a pack (SPEC F130.5, STORY-337, PLAN T303). See this
    /// class's own UNINSTALL IS GUARD-FREE remarks for why this carries no referenced-by check and
    /// never touches <c>station.settings</c>. With the pack found, 204: the row is gone. Loud either
    /// way (mirrors <see cref="AvatarPackController.Uninstall"/>'s own logging posture): INFO on an
    /// actual delete naming the slug, a genuinely unknown slug is a plain 404 with nothing to log.
    /// </summary>
    [HttpDelete("{slug}")]
    public async Task<IActionResult> Uninstall(string slug, CancellationToken ct)
    {
        if (slug.Length > CatalogInstallShell.MaxSlugLength)
            return BadRequest(CatalogInstallShell.SlugTooLongProblem(slug.Length));

        if (!CatalogInstallShell.SlugFormat().IsMatch(slug))
            return BadRequest(CatalogInstallShell.BadSlugProblem(slug));

        var deleted = await iconPackStore.DeleteAsync(slug, ct);
        if (!deleted)
            return NotFound(CatalogInstallShell.UnknownInstalledPackProblem(CatalogEntryKind.Icon, slug));

        logger.LogInformation("Icon pack uninstalled slug={Slug}", LogSafeText.Sanitize(slug));
        return NoContent();
    }

    // ── Library listing (GET /api/icon-packs) ───────────────────────────────

    /// <summary>
    /// GET /api/icon-packs — every installed pack (SPEC F130.4, STORY-337, PLAN T303): the settings
    /// dropdown's own data source (<c>Station:IconPack</c>, fed live via
    /// <see cref="StationSettingsAllowlist.IconPackChoices"/>) and the future Wardrobe Icons tab's
    /// listing route (PLAN T304). NO <see cref="CommunityCatalogAccessor.IsEnabled"/> GATE —
    /// DELIBERATE (mirrors <see cref="AvatarPackController.List"/>'s own remarks verbatim): this action
    /// lists what is ALREADY INSTALLED, station-local state that outlives the catalog. Ordered by slug
    /// (ordinal) for a stable, deterministic listing.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var packs = await iconPackStore.GetAllAsync(ct);
        return Ok(packs.OrderBy(pack => pack.Slug, StringComparer.Ordinal).Select(ToSummaryDto).ToArray());
    }

    static IconPackSummaryDto ToSummaryDto(IconPack pack)
    {
        var iconCount = IconPackDefinitionParser.Validate(Encoding.UTF8.GetBytes(pack.Definition)) is IconPackValidationResult.Valid valid
            ? valid.Definition.Icons.Count
            : 0;
        return new IconPackSummaryDto(pack.Slug, iconCount, pack.ImportedFrom, pack.ImportedAt);
    }

    // ── Active pack (GET /api/icon-packs/active) ─────────────────────────────

    /// <summary>
    /// GET /api/icon-packs/active — the currently activated pack's own canonical definition document
    /// (SPEC F130.3/F130.4, STORY-337, PLAN T303), for a future renderer (PLAN T304) to map into the
    /// admin chrome. <c>204 No Content</c> — never an error — for every "no active pack" shape:
    /// <c>Station:IconPack</c> unset (house icons, the F130.4 default), or set to a slug this store no
    /// longer has installed (the F130.5 fail-open uninstall — see this class's own UNINSTALL IS
    /// GUARD-FREE remarks). Reads <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> fresh per
    /// request (SPEC F130.4's own Live apply mode), so a <c>PUT /api/settings</c> or a pack uninstall
    /// reaches the very next request with no api restart.
    ///
    /// <para>
    /// Re-validates the STORED definition before serving it (defensive, should never fail — only this
    /// controller's own <see cref="Install"/> ever writes <c>station.icon_pack.definition</c>, and only
    /// via <see cref="IconPackDefinitionSerializer.Serialize"/>'s own canonical output) rather than
    /// trusting a jsonb column blindly — mirrors <see cref="AvatarPackController.ToSummaryDto"/>'s own
    /// "degrade, never 500, on a should-never-happen re-parse failure" posture, WARN-logged here since
    /// a persisted row failing re-validation is a genuine anomaly worth an operator's attention, unlike
    /// an ordinary dangling slug.
    /// </para>
    /// </summary>
    [HttpGet("active")]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        var activeSlug = stationMonitor.CurrentValue.IconPack;
        if (string.IsNullOrEmpty(activeSlug))
            return NoContent();

        var pack = await iconPackStore.GetBySlugAsync(activeSlug, ct);
        if (pack is null)
            return NoContent();

        if (IconPackDefinitionParser.Validate(Encoding.UTF8.GetBytes(pack.Definition)) is not IconPackValidationResult.Valid)
        {
            logger.LogWarning("Stored icon pack failed re-validation slug={Slug}", LogSafeText.Sanitize(activeSlug));
            return NoContent();
        }

        return Content(pack.Definition, "application/json");
    }
}
