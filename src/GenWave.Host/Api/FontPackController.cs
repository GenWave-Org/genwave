using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Catalog;
using GenWave.Host.Options;
using GenWave.Host.Theming;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/fonts/{slug}/install</c> (SPEC F104.5, STORY-282, PLAN T199) — installs a
/// Dean-curated font pack from the Community Catalog's <c>font</c> kind into this station's own
/// library (<c>station.font_pack</c>+<c>_face</c>, db/32). F79-shell idioms, reused deliberately
/// (mirrors <see cref="ThemesImportController"/>'s own remarks): the same auth pairing
/// (<see cref="AdminSurfaceAttribute"/> + <see cref="AuthorizationPolicies.Settings"/>), the same
/// catalog-slug vocabulary (<see cref="CatalogIndexValidator.SlugSegment"/>), and the same
/// deserialization-IS-validation posture applied to the fetched manifest instead of a request body.
///
/// <para>
/// <b>NO REQUEST BODY, by design.</b> Unlike <see cref="ThemesImportController.Import"/> or
/// <see cref="PersonaController.Import"/>, this route accepts nothing from the caller beyond the
/// route <c>slug</c> — the server fetches EVERY byte it stores through the guarded door
/// (<see cref="CatalogProxyService"/>), never trusting anything a request body could smuggle in. A
/// pack has no file-upload or authored-in-place path (SPEC F104.5): the catalog install route is the
/// ONLY door <c>station.font_pack</c> ever opens.
/// </para>
///
/// <para>
/// <b>Gate order.</b> Route slug format (400) → catalog kill-switch (404, bare — mirrors
/// <see cref="CatalogController"/>'s own disabled posture) → resolve the entry
/// (<see cref="CatalogProxyService.GetEntryAsync"/>: unknown slug or a non-font kind ⇒ 404;
/// unreachable ⇒ 503, see this class's own UNREACHABLE remarks; a withheld manifest/meta ⇒ 502) →
/// fetch and hash-verify EVERY declared asset, one at a time (<see cref="CatalogProxyService.GetAssetAsync"/>:
/// any withheld asset ⇒ 502, NOTHING stored), the app-side pack-bytes ceiling (400, see
/// <see cref="MaxPackBytes"/>'s own remarks) checked against the RUNNING total after EACH one — refusing
/// (and fetching no further assets) the instant it is crossed, never only after the whole loop completes
/// (review finding N1) → parse the manifest
/// (<see cref="CatalogFontManifestSerializer.Deserialize"/>: reject ⇒ 400) → cross-check the
/// manifest's own <c>files[]</c> against what was actually fetched, rejecting a duplicate filename
/// within <c>files[]</c> itself before it ever reaches the store (400 on either — review finding N2) →
/// ONE <see cref="IFontPackStore.UpsertAsync"/> call (409 on a cross-pack filename collision — see this
/// class's own COLLISION remarks) → <see cref="InstalledFontCatalog.ReloadAsync"/> (see this class's
/// own "Rebuild after write" remarks) → 200.
/// </para>
///
/// <para>
/// <b>UNREACHABLE IS 503, NOT THE GRACEFUL-200 SHAPE</b> (a deliberate deviation from
/// <see cref="CatalogController"/>'s own browse-route posture, T199). This is a WRITE, not a page
/// render: <see cref="CatalogController.Index"/>/<see cref="CatalogController.Entry"/> stay 200 with
/// an embedded flag because "nothing to show yet" is a legitimate, silent state for a shelf; a POST
/// that either genuinely installs a pack or does not has no such silent middle state to embed a flag
/// in, so this route follows <see cref="CatalogController.Asset"/>'s own binary-route precedent
/// instead (that action's own remarks: "a binary response has no JSON envelope to carry that signal
/// in… a real non-2xx status rather than state embedded in a 200"). This response body IS a small
/// JSON envelope, unlike <c>Asset</c>'s raw bytes, but the same reasoning holds: an install attempt
/// that could not even reach the catalog is a real failure to report to an operator who just clicked
/// "install", not a page to silently render degraded.
/// </para>
///
/// <para>
/// <b>THE 23505 MAPPING</b> (T198 review obligation; moved BEHIND the repository seam at gh-#406
/// slice 2 — the L2 Postgres-confinement law, ARCHITECTURE.md "Architecture governance"; this class no
/// longer references Npgsql at all). <c>station.font_pack_face.file</c> is UNIQUE across every
/// installed pack, not scoped per-pack (db/32) — two DIFFERENT catalog packs shipping a same-named
/// face is a real, if rare, possibility this route must fail closed on rather than 500.
/// <c>FontPackRepository.UpsertAsync</c>'s own single transaction means a mid-upsert Postgres
/// unique-violation has ALREADY rolled back everything (the pack row insert/update included) by the
/// time that repository's own <c>catch</c> runs — never a partial pack — and this action never sees
/// that exception, or its own internal detail text, at all: <see cref="IFontPackStore.UpsertAsync"/>
/// returns a <see cref="FontPackUpsertResult"/> whose <see cref="FontPackUpsertResult.FileCollision"/>
/// case already carries the actual colliding file and its owning pack slug — the repository's own
/// re-read of <see cref="IFontPackStore.GetAllAsync"/> (one extra query, only ever run on this rare
/// failure path), never the raw exception detail (F15.7's "no internal detail in a body" posture,
/// mirrors <see cref="CatalogController"/>'s own <c>WithheldProblem</c>). This action's own
/// <see cref="FileCollisionProblem(FontPackUpsertResult.FileCollision)"/> maps that already-sanitized
/// case straight to 409, falling back to a generic refusal when the repository's own lookup did not
/// resolve cleanly (<see cref="FontPackUpsertResult.FileCollision"/>'s own remarks).
/// </para>
///
/// <para>
/// <b>Rebuild after write (SPEC F104.6/F104.8, PLAN T200) — with <see cref="CancellationToken.None"/>,
/// deliberately (the <see cref="ThemesImportController"/>/T184 review F1 precedent).</b>
/// <see cref="InstalledFontCatalog.ReloadAsync"/> runs once, on the SAME DI'd singleton
/// <see cref="InstalledFontCatalogLoadHostedService"/> warms at boot — the only way an install reaches
/// every already-running request handler (the widened <c>GET /fonts/{file}</c> route) with no process
/// restart. Runs only on the success path, AFTER <see cref="IFontPackStore.UpsertAsync"/> has already
/// committed (including past the 23505-collision <see langword="switch"/> above, which returns before
/// reaching this line) — the write is no longer this request's to abandon, so passing this method's own
/// <paramref name="ct"/> here would let a client disconnecting mid-rebuild cancel it for no reason tied
/// to the write's own correctness: <see cref="InstalledFontCatalog.ReloadAsync"/>'s own
/// <see langword="catch"/> would swallow that as an ordinary reload failure and keep serving the
/// PREVIOUS snapshot (SPEC F104.8's offline floor, correctly triggered for a REAL store fault) — stale
/// for a request that merely stopped listening rather than anything wrong with
/// <c>station.font_pack</c>(+<c>_face</c>). <see cref="CancellationToken.None"/> makes the rebuild run
/// to completion regardless of who is still connected, which is what a committed write demands.
/// </para>
///
/// <para>
/// <b>STORED FAMILY/STYLE ARE UNBOUNDED — REVIEWER OBLIGATION FOR T200/T203.</b>
/// <see cref="CatalogFontManifestSerializer.Deserialize"/> only checks <c>manifest.Family</c> and each
/// <c>files[].style</c> are non-empty (<c>{ Length: > 0 }</c>) before this route writes them VERBATIM
/// into <c>station.font_pack.family</c>/<c>station.font_pack_face.style</c> — unlike the OPTIONAL
/// shelf-listing <c>family</c> field on an index.json entry itself, which
/// <see cref="CatalogIndexValidator"/>'s own <c>TryParseFamily</c> bounds to a real CSS-family shape
/// (regex + length ceiling) before it ever reaches a response. The index-side gate exists; the STORED
/// side deliberately does not (T199 shipped no consumer that reads either column back into CSS — see
/// <see cref="IFontPackStore"/>'s own "ships dark" remarks). <b>Still true after T200:</b> the widened
/// <c>GET /fonts/{file}</c> route (<see cref="InstalledFontCatalog.TryGetFace"/>) serves an installed
/// face's raw BYTES by file name and interpolates neither column into CSS — this obligation is
/// re-recorded on <see cref="InstalledFontCatalog"/>'s own remarks (its first read consumer) rather
/// than discharged here, so T203's library page and T206's editor pickers — the tasks that actually
/// put a face's family/style into a stylesheet or a picker label — cannot miss it. Whichever reaches
/// for either column in a CSS context first MUST NOT trust it as CSS-safe merely because it came from
/// this store — apply the same bound+shape discipline <c>TryParseFamily</c> already established for
/// the index-side field, or an equivalent CSS-injection-safe escape/allowlist, first.
/// <b>T203 compliance:</b> <see cref="List"/> is this obligation's first library-page consumer — its
/// own <see cref="FontLibraryPackDto"/>/<see cref="FontLibraryFaceDto"/> wire carries
/// <c>family</c>/<c>style</c> verbatim (still no bound/escape applied at THIS layer, deliberately —
/// see those DTOs' own remarks for why), and the Admin UI's library page renders both as plain React
/// text nodes ONLY, never interpolated into a stylesheet or inline <c>style</c> attribute.
/// <b>T206 compliance (review finding F2 — the RECORD was wrong, not the code): <c>Family</c> IS
/// discharged; <c>Style</c> stays undischarged but is moot for this consumer.</b> The editor's role
/// pickers (<see cref="Assignable"/> below, <c>EditorClient.tsx</c>) DO let an operator route an
/// installed pack's stored <c>Family</c> all the way into a real <c>font-family: "…"</c> declaration —
/// assigning a face composes it into the remix manifest, POSTed to
/// <see cref="ThemePreviewController.Preview"/> and <see cref="ThemesSaveAsOwnController.SaveAsOwn"/>
/// (PLAN T207), both of which call <see cref="ThemeManifestParser.Parse"/> BEFORE
/// <see cref="ThemeCssComposer"/> ever runs. <c>ThemeManifestParser</c>'s own <c>FontFamilyPattern</c>
/// re-validates EVERY family a posted manifest carries at that exact parse boundary — vendored,
/// installed, or otherwise, regardless of provenance — rejecting anything outside the CSS-safe shape
/// with a 400 the editor's own error state surfaces, never silently composing it. That parse-time gate
/// is what actually closes this obligation for <c>Family</c>, not a bound applied at this store or at
/// <see cref="InstalledFontCatalog"/>; the "apply the same bound+shape discipline… first" instruction
/// above is satisfied by an EQUIVALENT gate at the correct layer (the one place a family value ever
/// crosses into CSS), not a literal copy of <c>TryParseFamily</c> onto this store's own write path.
/// <c>Style</c>, by contrast, is never read by the editor at all — <c>EditorClient.tsx</c>'s
/// <c>assignedFace</c> hardcodes <c>"normal"</c> for every explicit assignment, and an unassigned role
/// passes the BASE THEME's own already-parsed <c>style</c> through untouched, never a pack's stored
/// one — so <c>FontPackFace.Style</c>'s "unbounded, don't trust it" obligation remains factually true
/// but is MOOT for this consumer, and carries forward unchanged to whichever future consumer, if any,
/// ever reads a pack's stored <c>Style</c> into a CSS context.
/// </para>
/// </summary>
[ApiController]
[Route("api/fonts")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class FontPackController(
    CatalogProxyService catalogProxyService,
    CommunityCatalogAccessor catalogAccessor,
    IFontPackStore fontPackStore,
    InstalledFontCatalog installedFontCatalog,
    ILogger<FontPackController> logger) : ControllerBase
{
    /// <summary>
    /// The app-side backstop over what this route actually STORES (T198 review obligation — the
    /// store itself bounds nothing; <c>station.font_pack_face</c> has no CHECK on <c>byte_size</c>).
    /// The REAL 200 KiB (204,800-byte) per-pack ceiling is enforced upstream, once, at catalog CI
    /// publish time (SPEC F104.2, genwave-catalog's <c>validate.py</c>, PLAN T195) — this constant is
    /// this app's OWN re-assertion of the identical number as defense-in-depth against a
    /// stale/compromised/hand-edited index this station's transport would otherwise fetch and store
    /// without complaint. Summed over EVERY asset the entry declares
    /// (<see cref="CatalogInstallShell.FetchAllAssetsAsync"/> — the woff2 face(s) AND the pack's OFL
    /// licence text this route fetches but never stores, see <see cref="BuildFaces"/>'s own remarks),
    /// mirroring catalog CI's own "summed asset bytes" definition (SPEC F104.2) exactly, not a narrower
    /// "faces only" sum a future drift between the two ceilings could otherwise hide. Checked against
    /// the RUNNING total INSIDE <see cref="CatalogInstallShell.FetchAllAssetsAsync"/>'s own fetch loop,
    /// not after it — see that method's own EARLY CUTOFF remarks (review finding N1) for why summing
    /// only after every asset is already fetched would let a hand-edited index buffer far past this
    /// ceiling before refusing.
    ///
    /// <para>
    /// This constant's home is HERE, deliberately — a font PACK ceiling, not the pre-existing
    /// <c>ThemeFontProvenanceValidator.PerThemeByteCeilingBytes</c> (a font THEME's summed ceiling,
    /// enforced across a different set of rows for a different rule). FONTS.md documents that
    /// SEPARATE per-theme rule at the SAME 200 KiB (204,800-byte) magnitude — a coincidence of scale,
    /// not a shared constant; there is exactly one consumer of THIS number today, so it lives as a
    /// plain <see langword="const"/> on the one controller that enforces it rather than a
    /// prematurely-shared home. Update ARCHITECTURE.md/SPEC.md F104.2 and this constant together if
    /// either ceiling's number ever changes.
    /// </para>
    /// </summary>
    public const long MaxPackBytes = 200 * 1024;

    /// <summary>
    /// POST /api/fonts/{slug}/install — see this class's own remarks for the full gate order and the
    /// reasoning behind each one.
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
            catalogProxyService, CatalogEntryKind.Font, slug, ct);
        if (entryError is not null)
            return entryError;
        if (entryContent is not { } content)
            throw new UnreachableException("CatalogInstallShell.ResolveEntryAsync returned neither an error nor content.");

        var (assetsError, fetchedAssetsOrNull) = await CatalogInstallShell.FetchAllAssetsAsync(
            catalogProxyService, slug, content,
            new CatalogInstallShell.PackFetchPolicy(CatalogEntryKind.Font, CatalogProxyService.MaxAssetBytes, MaxPackBytes, "SPEC F104.2"),
            ct);
        if (assetsError is not null)
            return assetsError;
        if (fetchedAssetsOrNull is not { } fetchedAssets)
            throw new UnreachableException("CatalogInstallShell.FetchAllAssetsAsync returned neither an error nor a fetched-asset map.");

        var manifest = CatalogFontManifestSerializer.Deserialize(content.ManifestJson);
        if (manifest is null)
            return BadRequest(CatalogInstallShell.MalformedManifestProblem(CatalogEntryKind.Font, slug));

        var (facesError, facesOrNull) = BuildFaces(slug, manifest, fetchedAssets);
        if (facesError is not null)
            return facesError;
        if (facesOrNull is not { } faces)
            throw new UnreachableException("BuildFaces returned neither an error nor a face list.");

        var upsertResult = await fontPackStore.UpsertAsync(slug, manifest.Family, content.ManifestJson, slug, faces, ct);
        switch (upsertResult)
        {
            case FontPackUpsertResult.Upserted:
                break;
            case FontPackUpsertResult.FileCollision collision:
                return Conflict(FileCollisionProblem(collision));
            default:
                throw new UnreachableException($"Unhandled {nameof(FontPackUpsertResult)} case.");
        }

        // CancellationToken.None, deliberately — see this method's own "Rebuild after write" remarks
        // (the T184/ThemesImportController lesson): the upsert above has already committed, so the
        // rebuild is no longer this request's to abandon.
        await installedFontCatalog.ReloadAsync(CancellationToken.None);

        logger.LogInformation(
            "Font pack installed slug={Slug} family={Family} faceCount={FaceCount}",
            LogSafeText.Sanitize(slug), LogSafeText.Sanitize(manifest.Family), faces.Count);

        return Ok(new FontPackInstallResponse(slug, manifest.Family, faces.Select(f => f.File).ToArray(), slug));
    }

    // ── Uninstall (DELETE /api/fonts/{slug}) ───────────────────────────────

    /// <summary>
    /// DELETE /api/fonts/{slug} — uninstalls a pack (SPEC F104.14, STORY-288, PLAN T208). Refused with
    /// 409, naming every referencing theme, while any saved/imported <c>station.theme</c> row still
    /// references one of this pack's own faces (the persona-delete FK-guard precedent, applied where
    /// the reference lives inside opaque jsonb rather than a real foreign key) — see
    /// <see cref="IFontPackStore.DeleteAsync"/>'s own remarks for how the guard is enforced atomically,
    /// inside the delete statement itself, never as an advisory pre-check this action could race past.
    /// With no reference, 204: the pack row and every one of its faces are gone (db/32's own
    /// <c>ON DELETE CASCADE</c>), and the SAME post-write rebuild <see cref="Install"/> already performs
    /// (<see cref="InstalledFontCatalog.ReloadAsync"/>) runs again here — <c>GET /fonts/{file}</c> stops
    /// serving this pack's faces on the very next request (SPEC F104.14's own "next request" wording),
    /// never waiting on a process restart.
    ///
    /// <para>
    /// <b>Loud either way (M1 carry-forward — no silent-vanish path).</b> Every outcome is logged: an
    /// uninstall that actually removes a pack (INFO, naming the slug — mirrors <see cref="Install"/>'s
    /// own INFO line), and a refusal (WARN, naming the slug and how many themes blocked it — mirrors
    /// <c>PersonaController.Delete</c>'s own WARN-on-block precedent). A genuinely unknown slug is
    /// neither — a plain 404, the same "nothing to log" posture <see cref="CatalogInstallShell.ResolveEntryAsync"/>'s
    /// own unknown-pack 404 already carries.
    /// </para>
    /// </summary>
    [HttpDelete("{slug}")]
    public async Task<IActionResult> Uninstall(string slug, CancellationToken ct)
    {
        if (slug.Length > CatalogInstallShell.MaxSlugLength)
            return BadRequest(CatalogInstallShell.SlugTooLongProblem(slug.Length));

        if (!CatalogInstallShell.SlugFormat().IsMatch(slug))
            return BadRequest(CatalogInstallShell.BadSlugProblem(slug));

        var result = await fontPackStore.DeleteAsync(slug, ct);

        switch (result)
        {
            case FontPackDeleteResult.Deleted:
                // CancellationToken.None, deliberately — see Install's own "Rebuild after write"
                // remarks: the delete above has already committed, so the rebuild is no longer this
                // request's to abandon.
                await installedFontCatalog.ReloadAsync(CancellationToken.None);
                logger.LogInformation("Font pack uninstalled slug={Slug}", LogSafeText.Sanitize(slug));
                return NoContent();
            case FontPackDeleteResult.NotFound:
                return NotFound(CatalogInstallShell.UnknownInstalledPackProblem(CatalogEntryKind.Font, slug));
            case FontPackDeleteResult.Referenced referenced:
                logger.LogWarning(
                    "Font pack uninstall refused slug={Slug} referencedByCount={Count}",
                    LogSafeText.Sanitize(slug), referenced.ThemeSlugs.Count);
                return Conflict(ReferencedProblem(slug, referenced.ThemeSlugs));
            default:
                throw new UnreachableException($"Unhandled {nameof(FontPackDeleteResult)} case.");
        }
    }

    // ── Library listing (GET /api/fonts) ────────────────────────────────────

    /// <summary>
    /// GET /api/fonts — every installed pack (SPEC F104.7, STORY-284, PLAN T203): family, faces
    /// (file/style/byteSize), licence/sourceUrl/version/subset, and "Installed · ⟨slug⟩ · ⟨date⟩"
    /// provenance (db/25 pattern) for the Admin UI's library page. Reads straight off
    /// <see cref="IFontPackStore.GetAllAsync"/> — METADATA ONLY, no face bytes ever reach this wire
    /// (that store's own remarks: a listing has no use for a face's raw payload). Ordered by slug
    /// (ordinal) for a stable, deterministic listing — <see cref="IFontPackStore.GetAllAsync"/>'s own
    /// remarks guarantee no particular order from the store itself.
    ///
    /// <para>
    /// <b>Route-set obligation (T200 review finding N7).</b> This is the first <c>GET</c> under
    /// <c>api/fonts</c> — <c>Story278_ThemeCatalogIsolation.cs</c>'s own route-set pin (its
    /// <c>ScenarioNoNewPublicRoute.KnownCatalogAndThemeRoutes</c>) is extended to include it, with the
    /// SAME class-level <see cref="AdminSurfaceAttribute"/>+<see cref="AuthorizationPolicies.Settings"/>
    /// pairing every route on this controller already carries — a plain <c>[HttpGet]</c> action
    /// change would otherwise trip that file's exact-match assertion, exactly as the finding asked
    /// for.
    /// </para>
    ///
    /// <para>
    /// <b>NO <see cref="CommunityCatalogAccessor.IsEnabled"/> GATE — DELIBERATE (T203 review finding
    /// F1, SPEC F104.8).</b> Unlike <see cref="Install"/>, this action never checks the catalog kill
    /// switch. The library lists what is ALREADY INSTALLED — <c>station.font_pack</c>(+<c>_face</c>)
    /// rows this station wrote for itself at some past install, station-local state that outlives the
    /// catalog exactly the way an already-loaded <c>/fonts/{file}</c> face keeps serving with the
    /// catalog or the DB gone (F104.8's retention posture, <see cref="InstalledFontCatalog"/>'s own
    /// offline floor). <see cref="CommunityCatalogAccessor.IsEnabled"/> gates the CATALOG surface —
    /// routes that reach out to (or exist only because of) the Community Catalog origin, which is
    /// every OTHER route on this controller plus every route on <see cref="CatalogController"/> — not
    /// the station's own inventory of what it already chose to keep. Disabling the catalog turns off
    /// DISCOVERY of new packs, never REMEMBRANCE of installed ones: with the switch off, this action
    /// still 200-lists every installed pack while <see cref="Install"/> still 404s bare — that
    /// divergence is pinned by name in <c>Story284_FontPackLibrary.cs</c>'s own
    /// <c>ScenarioTheCatalogKillSwitchDoesNotGateTheLibrary</c>.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var packs = await fontPackStore.GetAllAsync(ct);
        return Ok(packs.OrderBy(pack => pack.Slug, StringComparer.Ordinal).Select(ToLibraryDto).ToArray());
    }

    /// <summary>See <see cref="FontLibraryPackDto"/>'s own remarks for why <see cref="FontLibraryPackDto.License"/>/
    /// <see cref="FontLibraryPackDto.SourceUrl"/>/<see cref="FontLibraryPackDto.Version"/>/<see cref="FontLibraryPackDto.Subset"/>
    /// are the only fields this re-parse can affect.</summary>
    static FontLibraryPackDto ToLibraryDto(FontPack pack)
    {
        var manifest = CatalogFontManifestSerializer.Deserialize(pack.Definition);
        return new FontLibraryPackDto(
            pack.Slug,
            pack.Family,
            pack.Faces.Select(face => new FontLibraryFaceDto(face.File, face.Style, face.ByteSize)).ToArray(),
            manifest?.License,
            manifest?.SourceUrl,
            manifest?.Version,
            manifest?.Subset,
            pack.ImportedFrom,
            pack.ImportedAt);
    }

    // ── Assignable faces (GET /api/fonts/assignable) ──────────────────────────

    /// <summary>
    /// GET /api/fonts/assignable — the v2 editor's role pickers' ENTIRE assignable set in one call
    /// (SPEC F104.11, STORY-286, PLAN T206; widened at T206 review finding F4; renamed from
    /// <c>GET /api/fonts/vendored</c>/<see cref="Api.AssignableFaceDto"/>'s former
    /// <c>VendoredFontDto</c> name at PLAN T207 review carry-in 1 — the old name promised "vendored
    /// only" while the response has been vendored ∪ installed since T206): vendored ∪ installed,
    /// one row per FAMILY, each carrying the representative UPRIGHT src a role assignment composes
    /// with. Reads the SAME two sources <see cref="ThemeFontProvenanceValidator"/>'s own widened
    /// callers ultimately trust — <see cref="FontProvenanceCatalog.Default"/> for the vendored half
    /// (the SAME static singleton <see cref="ThemePreviewController"/>/<see cref="ThemesImportController"/>
    /// already read for the widened font law), <see cref="IFontPackStore.GetAllAsync"/> for the
    /// installed half (the SAME call <see cref="List"/> above already makes, and the same call
    /// <see cref="InstalledFontCatalog.ReloadAsync"/> builds its own snapshot from) — never a second,
    /// independently-maintained list.
    ///
    /// <para>
    /// <b>ONE representative-face heuristic, not two (T206 review finding F4).</b> Before this fix, the
    /// vendored half was filtered server-side by an ordinal filename-substring "italic" check while
    /// <c>EditorClient.tsx</c> separately re-derived the installed half by <c>style === "normal"</c>
    /// with its own <c>faces[0]</c> fallback — two DIFFERENT heuristics, derived in two different
    /// languages, that could disagree with each other and with
    /// <see cref="ThemeFontProvenanceValidator"/>. <see cref="RepresentativeVendoredSrc"/>/
    /// <see cref="RepresentativeInstalledFace"/> below are now the ONLY place either determination is
    /// made, server-side, once; the client consumes this DTO array verbatim and derives nothing of its
    /// own. The vendored half still resorts to the filename-substring heuristic —
    /// <see cref="VendoredFontFace"/> carries no explicit style flag to check instead — but there is
    /// now exactly one such check in the whole app, not two that could drift apart. A family present in
    /// BOTH sets (not a real case today — Dean's own curation keeps them disjoint, SPEC F104.16) keeps
    /// its VENDORED row: <c>VendoredFaceOptions()</c> is concatenated before
    /// <c>InstalledFaceOptions(...)</c>, and the dedupe-by-family group below keeps the first.
    /// </para>
    ///
    /// <para>
    /// <b>Route-set obligation</b> — unchanged by this widening: this is still the SECOND <c>GET</c>
    /// under <c>api/fonts</c>, still pinned by name in <c>Story278_ThemeCatalogIsolation.cs</c>'s own
    /// route-set pin (<c>ScenarioNoNewPublicRoute.KnownCatalogAndThemeRoutes</c>) and
    /// <c>Story283_InstalledFontServing.cs</c>'s own <c>ExpectedFontRoutes</c>, with the SAME
    /// class-level <see cref="AdminSurfaceAttribute"/>+<see cref="AuthorizationPolicies.Settings"/>
    /// pairing every route on this controller already carries — the URL and its auth posture are
    /// untouched, only the response SHAPE widened.
    /// </para>
    ///
    /// <para>
    /// <b>NO <see cref="CommunityCatalogAccessor.IsEnabled"/> GATE — the SAME reasoning as
    /// <see cref="List"/>'s own remarks, applied to a UNION of embedded (vendored) and stored
    /// (installed) data rather than either alone.</b> Neither source has a Community Catalog origin
    /// this route depends on, so there is no reachability axis for the kill switch to gate — disabling
    /// the catalog (an empty <c>Community:CatalogIndexUrl</c>) never changes this route's answer while
    /// <see cref="Install"/> still 404s bare. Previously documented but NOT Fact-pinned (T206 review
    /// finding F1 — the sibling <see cref="List"/> route's own divergence WAS pinned,
    /// <c>Story284_FontPackLibrary.cs</c>'s own <c>ScenarioTheCatalogKillSwitchDoesNotGateTheLibrary</c>,
    /// this route's was not); now pinned by name, alongside <c>GET /api/themes</c>'s own identical
    /// posture, in <c>Story286_EditorComposesTheRemix.cs</c>'s own
    /// <c>ScenarioTheCatalogKillSwitchDoesNotGateTheEditorReads</c>.
    /// </para>
    /// </summary>
    [HttpGet("assignable")]
    public async Task<IActionResult> Assignable(CancellationToken ct)
    {
        var packs = await fontPackStore.GetAllAsync(ct);
        var assignable = VendoredFaceOptions()
            .Concat(InstalledFaceOptions(packs))
            .GroupBy(face => face.Family, StringComparer.Ordinal)
            .Select(group => group.First()) // vendored listed first — a family in both keeps its vendored row
            .OrderBy(face => face.Family, StringComparer.Ordinal)
            .ToArray();
        return Ok(assignable);
    }

    /// <summary>One <see cref="AssignableFaceDto"/> per curated family — see <see cref="Assignable"/>'s
    /// own "ONE representative-face heuristic" remarks for why an italic file is filtered out here
    /// rather than carried through.</summary>
    static IEnumerable<AssignableFaceDto> VendoredFaceOptions() =>
        FontProvenanceCatalog.Default.BySrc.Values
            .GroupBy(face => face.Family, StringComparer.Ordinal)
            .Select(group => new AssignableFaceDto(group.Key, RepresentativeVendoredSrc(group)));

    /// <summary>The upright face's own src for a family that may carry an italic sibling too — falls
    /// back to whichever face is first when every face in the group happens to look italic (never
    /// actually true of today's provenance record, defensive only).</summary>
    static string RepresentativeVendoredSrc(IEnumerable<VendoredFontFace> familyFaces) =>
        (familyFaces.FirstOrDefault(face => !face.File.Contains("italic", StringComparison.Ordinal))
            ?? familyFaces.First()).Src;

    /// <summary>One <see cref="AssignableFaceDto"/> per installed pack (SPEC F104's own "role-agnostic,
    /// one family per pack" shape) — silently skips a pack with no faces at all via
    /// <see cref="RepresentativeInstalledFace"/>'s own <see langword="null"/> return (SPEC F104.5's
    /// non-empty <c>files[]</c> install gate means this never actually happens in practice; defensive
    /// only, the same posture <c>EditorClient.tsx</c>'s own former client-side projection documented
    /// before this moved server-side).</summary>
    static IEnumerable<AssignableFaceDto> InstalledFaceOptions(IReadOnlyList<FontPack> packs) =>
        packs.Select(RepresentativeInstalledFace).OfType<AssignableFaceDto>();

    /// <summary>The installed pack's OWN "normal"-style face, falling back to its first face — the
    /// SAME representative-face rule <see cref="RepresentativeVendoredSrc"/> applies to the vendored
    /// half, expressed against <see cref="FontPackFace.Style"/>'s real, manifest-declared value instead
    /// of a filename guess (an installed pack's style is recorded truth, not inferred).</summary>
    static AssignableFaceDto? RepresentativeInstalledFace(FontPack pack)
    {
        var face = pack.Faces.FirstOrDefault(f => f.Style == FontPackFaceInput.NormalStyle) ?? pack.Faces.FirstOrDefault();
        return face is null ? null : new AssignableFaceDto(pack.Family, $"/fonts/{face.File}");
    }

    // ── Entry resolution + asset fetch (SHARED SHELL, PLAN T293 review finding S6) ──────────────────
    //
    // The entry-kind resolution switch and the per-asset fetch-and-hash-verify loop (including the
    // EARLY CUTOFF discipline this class's own N1 review finding established — MaxPackBytes checked
    // against the RUNNING total INSIDE the loop, refusing the instant it is crossed, never only after
    // every declared asset is already buffered) both moved to CatalogInstallShell.ResolveEntryAsync/
    // FetchAllAssetsAsync once AvatarPackController became a second near-verbatim copy of both — see
    // that type's own remarks for the full reasoning, unchanged in substance from when it lived here.
    // Install (above) is the one caller of both.

    // ── Manifest cross-check ────────────────────────────────────────────────

    /// <summary>
    /// Cross-checks the parsed manifest's own <c>files[]</c> against what was actually fetched
    /// (SPEC F104.5's "manifest files ⊆ fetched assets" guard) and builds the write-side face list.
    /// Faces are woff2 ONLY — <paramref name="manifest"/>'s <c>files[]</c>, never the pack's OFL
    /// licence text asset <see cref="CatalogInstallShell.FetchAllAssetsAsync"/> fetched and
    /// hash-verified too but this method simply never reaches for: <c>station.font_pack_face</c> feeds the widened
    /// <c>/fonts/{file}</c> route (PLAN T200), which only ever serves faces, never licence prose — the
    /// licence stays catalog-side, still readable through the existing generic asset route
    /// (<c>GET /api/catalog/entries/{slug}/assets/{file}</c>) if an operator wants to read it.
    ///
    /// <para>
    /// <b>DUPLICATE <c>files[]</c> ENTRY (review finding N2).</b> <c>station.font_pack_face.file</c>
    /// is UNIQUE (db/32, the same constraint <c>FontPackRepository.UpsertAsync</c>'s own COLLISION
    /// handling maps to a <see cref="FontPackUpsertResult.FileCollision"/> for a CROSS-pack clash) — a
    /// manifest that lists the SAME file twice would otherwise reach
    /// <see cref="IFontPackStore.UpsertAsync"/> and die there too, but as a real Postgres 23505 with no
    /// OTHER pack actually owning the file: that repository's own lookup would find no owner and this
    /// action's own <see cref="FileCollisionProblem(FontPackUpsertResult.FileCollision)"/> would fall
    /// back to its generic, unhelpful refusal. Caught HERE instead, before a single byte reaches the
    /// store, with a precise 400 naming the duplicated filename.
    /// </para>
    /// </summary>
    (IActionResult? Error, IReadOnlyList<FontPackFaceInput>? Faces) BuildFaces(
        string slug, CatalogFontManifest manifest, Dictionary<string, CatalogInstallShell.CatalogFetchedAsset> fetchedAssets)
    {
        var faces = new List<FontPackFaceInput>(manifest.Files.Count);
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            if (!seenFiles.Add(file.File))
                return (BadRequest(DuplicateManifestAssetProblem(slug, file.File)), null);

            if (!fetchedAssets.TryGetValue(file.File, out var asset))
                return (BadRequest(CatalogInstallShell.UndeclaredManifestAssetProblem(CatalogEntryKind.Font, slug, file.File)), null);

            faces.Add(new FontPackFaceInput(file.File, asset.Bytes, asset.Sha256, file.Style));
        }

        return (null, faces);
    }

    // ── 23505 mapping (T198 review obligation; resolution moved to FontPackRepository at gh-#406
    //    slice 2 — this action only maps the already-sanitized case to a wire response) ────────────

    /// <summary>Dispatches an already-resolved <see cref="FontPackUpsertResult.FileCollision"/> (built
    /// by <c>FontPackRepository.UpsertAsync</c>'s own <c>catch</c>, see this class's own COLLISION
    /// remarks) to the naming 409 when both fields resolved cleanly, else the generic fallback —
    /// mirrors <see cref="FontPackUpsertResult.FileCollision"/>'s own "both null together" remarks.</summary>
    static ProblemDetails FileCollisionProblem(FontPackUpsertResult.FileCollision collision) =>
        collision is { File: { } file, OwnerSlug: { } ownerSlug }
            ? FileCollisionProblem(file, ownerSlug)
            : GenericFileCollisionProblem();

    // ── Helpers ──────────────────────────────────────────────────────────────
    //
    // The slug-format regex/cap, DisabledSurfaceResult, and the generic unknown-pack/catalog-
    // unavailable/withheld/pack-too-large/malformed-manifest/undeclared-asset ProblemDetails factories
    // all moved to CatalogInstallShell (PLAN T293 review finding S6) once AvatarPackController became a
    // second byte-identical copy of every one of them — see that type's own remarks. What remains here
    // is FONT-SPECIFIC: the referenced-by-theme guard (no avatar-side counterpart), the 23505
    // file-collision mapping, and the duplicate-files[]-entry check.

    // SPEC F104.14 — names every referencing theme by slug (the persona-delete precedent's own "name
    // every offending row" contract). Falls back to generic wording only on FontPackDeleteResult.
    // Referenced's own documented rare-empty race case (see that type's own remarks) — never a bare
    // "cannot be uninstalled" with no theme list when one is actually available.
    static ProblemDetails ReferencedProblem(string slug, IReadOnlyList<string> themeSlugs) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Font pack is referenced.",
        Detail = themeSlugs.Count > 0
            ? $"\"{slug}\" is still referenced by theme(s) {string.Join(", ", themeSlugs.Select(t => $"\"{t}\""))} " +
              "and cannot be uninstalled — remove or edit those themes first."
            : $"\"{slug}\" is still referenced by a theme and cannot be uninstalled.",
    };

    static ProblemDetails DuplicateManifestAssetProblem(string slug, string file) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Malformed font pack manifest.",
        Detail = $"\"{slug}\"'s manifest lists \"{file}\" more than once in files[].",
    };

    static ProblemDetails FileCollisionProblem(string file, string ownerSlug) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Font file already installed.",
        Detail = $"\"{file}\" is already installed by pack \"{ownerSlug}\" (font filenames are unique across every installed pack).",
    };

    static ProblemDetails GenericFileCollisionProblem() => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Font file already installed.",
        Detail = "One of this pack's face files is already installed by another pack.",
    };
}
