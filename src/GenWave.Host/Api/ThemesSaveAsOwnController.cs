using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Host.Catalog;
using GenWave.Host.Theming;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/themes/{slug}/save-as-own</c> — the v2 editor's Save-as-own action (SPEC F104.13,
/// STORY-287, PLAN T207): writes a complete <see cref="ThemeManifest"/> the editor composed CLIENT-SIDE
/// (the ephemeral remix — a base theme's palette plus role-assigned faces, <c>EditorClient.tsx</c>'s
/// own <c>buildRemixManifest</c>, SPEC F104.11/F104.12) into <c>station.theme</c> with
/// <c>imported_from</c> <see langword="null"/> — the RESERVED "authored, not imported" provenance value
/// <see cref="OwnerTheme"/>'s own remarks already document, finally given its first writer.
///
/// <para>
/// <b>station.theme's SECOND write route, deliberately its own controller.</b> Mirrors
/// <see cref="ThemesImportController"/>'s own "deliberately its own controller, not folded into an
/// existing one" posture (that class's own remarks) — a save is not an import wearing a different
/// query string: it carries no <c>catalogSlug</c>, no "file" fallback, and its ONE allowed provenance
/// value is the opposite of everything <see cref="ThemesImportController.Import"/> ever stamps. What
/// the two routes DO share — and share literally, not by convention — is every gate STORY-287 AC3
/// names ("same parse/law/ceiling/shipped-slug gates as import (same copy)"): both call the SAME
/// <see cref="ThemeWriteGate.RunAsync"/> (PLAN T207 review finding F1) — the one place each of those
/// gates' refusal text is built, so "byte-identical copy" holds by CONSTRUCTION rather than by two
/// hand-copied blocks a reviewer had to eyeball for drift. See that type's own remarks for the full
/// gate order and reasoning.
/// </para>
///
/// <para>
/// <b>Gate order.</b> <see cref="ThemeWriteGate.RunAsync"/>'s own shared pipeline (route slug format
/// 400 → shipped-slug reservation 409, F103.8 → bounded body read 413 → schema-major 400 →
/// deserialize-as-validation 400 → curated-font provenance/byte-ceiling 400, F103.10/F104.9) → THIS
/// route's own conditional upsert with <c>imported_from = null</c> (409 on conflict with an imported
/// row, SPEC F104.13, PLAN T207 review finding F2 / gh-#394 — see this controller's own "Fail-closed
/// overwrite" remarks below) → catalog rebuild (F103.7's "no restart" contract, same rebuild hook
/// import uses).
/// </para>
///
/// <para>
/// <b>Fail-closed overwrite (SPEC F104.13, PLAN T207 review finding F2 — Dean's standing fail-closed
/// preference, 2026-08-05; gh-#394 closed the race this paragraph used to leave open).</b> A save
/// targeting a slug that already holds an IMPORTED theme (a <see cref="OwnerTheme.ImportedFrom"/> that
/// is non-null) is REFUSED with 409 (<see cref="ImportProblems.SlugHoldsAnImportedTheme"/>) — an
/// authored save must never silently destroy another theme's own imported provenance, which an
/// unconditional upsert would do (the target row's <c>imported_from</c>/<c>imported_at</c> would both
/// be NULLed). Authored-OVER-authored — the operator re-saving onto a slug that already holds THEIR OWN
/// previously-saved theme (imported_from already <see langword="null"/>) — stays ALLOWED: that is
/// ordinary iteration on a theme this route itself created, the exact "re-save under the same name
/// replaces rather than duplicates" contract <c>EditorClient.tsx</c>'s own <c>handleSaved</c> remarks
/// already promise. A slug with NO existing row at all is, naturally, also allowed (a fresh insert,
/// nothing to overwrite). Checked AFTER the shared gate pipeline has already produced a valid,
/// law-passing manifest — a doomed-on-content request still fails on ITS OWN defect first, never masked
/// by an overwrite refusal it would have hit regardless.
/// </para>
///
/// <para>
/// gh-#394 — this guard used to be a read (<see cref="IThemeStore.GetBySlugAsync"/>) followed by a
/// separate write (<see cref="IThemeStore.UpsertAsync"/>), a TOCTOU window an import committing to the
/// same slug between the two calls could race: the read would see "no row / authored row", and the
/// unconditional write that followed would still clobber whatever the import had just committed. There
/// is no pre-check read anymore — <see cref="IThemeStore.SaveAsOwnAsync"/> IS the check, a single
/// atomic <c>INSERT … ON CONFLICT … DO UPDATE … WHERE</c> statement that refuses (returns
/// <see langword="false"/>, zero rows affected) in the exact same case the old pre-check refused, with
/// no gap between "look" and "leap" for a concurrent import to land in.
/// </para>
///
/// <para>
/// <b>Slug is the upsert key here too (mirrors <see cref="ThemesImportController"/>'s own "Slug is the
/// upsert key, not the manifest's own opinion" remarks).</b> <c>EditorClient.tsx</c>'s remix manifest
/// starts as a COPY of the base theme's own fields (<c>...base</c>, including its <c>slug</c>) — the
/// save affordance asks the operator for a NEW name/slug (STORY-287 AC1) and threads it into the POSTed
/// body before this route ever sees it, but this route does not trust that the client actually did so:
/// <see cref="ThemeWriteGate.RunAsync"/> re-stamps the manifest with the route <paramref name="slug"/>
/// before ever returning it — without this, a client that forgot to overwrite the copied <c>slug</c>
/// field would silently OVERWRITE the base theme's own row instead of creating a new one.
/// </para>
///
/// <para>
/// <b><c>imported_from</c> is ALWAYS <see langword="null"/> here — never a parameter, never derived
/// from anything the request carries.</b> Unlike <see cref="ThemesImportController.Import"/>'s
/// <c>catalogSlug</c>-or-"file" branch, this route has no provenance INPUT to branch on: SPEC F104.13
/// names <see langword="null"/> as the one value a save-as-own row is ever stamped with, so
/// <see cref="IThemeStore.SaveAsOwnAsync"/> below has no provenance parameter to accept at all — see
/// <see cref="GenWave.MediaLibrary.Station.ThemeRepository.SaveAsOwnAsync"/>'s own remarks for how the
/// store honours <see cref="Core.Domain.OwnerTheme"/>'s "<c>ImportedAt</c> is <see langword="null"/>
/// exactly when <c>ImportedFrom</c> is" invariant on this path.
/// </para>
///
/// <para>
/// <b>Base theme untouched (SPEC F104.13, STORY-287 AC2) — true by construction, not by a check this
/// route performs.</b> <see cref="IThemeStore.SaveAsOwnAsync"/> writes exactly one row, keyed by
/// <paramref name="slug"/> — the route slug the operator chose for the NEW theme, distinct from
/// whichever base theme's slug the remix was mixed from. This route never reads, let alone writes, any
/// slug other than <paramref name="slug"/> itself, so the base theme it was mixed from resolves
/// byte-identically after the save — pinned by name in <c>Story287_SaveAsOwn.cs</c>'s own
/// <c>ScenarioTheBaseThemeIsUntouched</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/themes")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class ThemesSaveAsOwnController(
    IThemeStore themeStore,
    ThemeCatalog themeCatalog,
    InstalledFontCatalog installedFontCatalog,
    CatalogProxyService catalogProxyService,
    ILogger<ThemesSaveAsOwnController> logger) : ControllerBase
{
    /// <summary>
    /// POST /api/themes/{slug}/save-as-own — see this controller's own remarks for the full gate order
    /// and why it shares <see cref="ThemeWriteGate.RunAsync"/> with
    /// <see cref="ThemesImportController.Import"/> rather than an independently re-derived copy.
    /// </summary>
    [HttpPost("{slug}/save-as-own")]
    [Consumes("application/json")]
    [RequestSizeLimit(BoundedImportBodyReader.MaxImportBytes)]
    public async Task<IActionResult> SaveAsOwn(string slug, CancellationToken ct)
    {
        var (refusal, manifestOrNull) = await ThemeWriteGate.RunAsync(
            Request, slug, themeCatalog, installedFontCatalog, catalogProxyService, ct);
        if (refusal is not null)
            return refusal;
        if (manifestOrNull is not { } normalized)
            throw new UnreachableException($"{nameof(ThemeWriteGate)}.{nameof(ThemeWriteGate.RunAsync)} returned neither a refusal nor a manifest.");

        // Fail-closed overwrite refusal (SPEC F104.13, PLAN T207 review finding F2, gh-#394) — see
        // this controller's own "Fail-closed overwrite" remarks for the full ruling. The write below IS
        // the check (an atomic conditional upsert, no separate pre-check read): it refuses (false, no
        // row touched) only when the slug already holds an IMPORTED row (non-null ImportedFrom); no row
        // at all, or a row this route itself authored before (ImportedFrom already null), both succeed.
        var saved = await themeStore.SaveAsOwnAsync(slug, ThemeManifestSerializer.Serialize(normalized), ct);
        if (!saved)
            return Conflict(ImportProblems.SlugHoldsAnImportedTheme(slug));

        // CancellationToken.None, deliberately — mirrors ThemesImportController.Import's own "Rebuild
        // after write" remarks: the write above has already committed, so the rebuild is no longer this
        // request's to abandon.
        await themeCatalog.ReloadOwnerThemesAsync(CancellationToken.None);

        // \A..\z-anchored long before this line (ThemeWriteGate.RunAsync's own slug-format gate), so no
        // control character can reach the template — mirrors ThemesImportController.Import's own
        // Sanitize call.
        logger.LogInformation("Theme saved as own slug={Slug}", LogSafeText.Sanitize(slug));

        return Ok(new ThemeSaveAsOwnResponse(normalized.Slug, normalized.Name));
    }
}
