using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Host.Catalog;
using GenWave.Host.Theming;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/themes/{slug}/import?catalogSlug=…</c> — imports a theme, the theme-kind sibling of
/// <see cref="PersonaController.Import"/> (SPEC F103.6, STORY-272, PLAN T184; ARCHITECTURE "Community
/// Catalog v2 → the theme kind"). Deliberately its OWN controller, not folded into
/// <see cref="PersonaController"/> or <see cref="CatalogController"/> — a distinct resource
/// (<c>station.theme</c>), even though the shell it reuses is identical.
///
/// <para>
/// <b>The F79 shell, reused deliberately.</b> Same auth pairing
/// (<see cref="AdminSurfaceAttribute"/> + <see cref="AuthorizationPolicies.Settings"/>), same
/// <see cref="BoundedImportBodyReader.MaxImportBytes"/> size cap enforced by both
/// <see cref="RequestSizeLimitAttribute"/> and <see cref="BoundedImportBodyReader.ReadBoundedBodyAsync"/>'s
/// own running-total read, same slug-format gate (composed from
/// <see cref="CatalogIndexValidator.SlugSegment"/> — the catalog's own slug vocabulary, since this
/// route is catalog-adjacent rather than persona-adjacent), and the same deserialization-IS-validation
/// posture: a <see cref="ThemeManifestException"/> from <see cref="ThemeManifestParser.Parse"/>
/// (malformed JSON, or any structural defect the parser's own load-time rules catch — missing
/// slug/name/author, an incomplete mode pair, an unsafe token/font shape) maps to 400, never an
/// unhandled 500.
/// </para>
///
/// <para>
/// <b>The shared gate pipeline, in TWO PHASES (SPEC F103.6/F103.10/F104.9, PLAN T184/T188/T205; PLAN
/// T207 review findings F1/B2) — <see cref="ThemeWriteGate"/>.</b>
/// <see cref="ThemeWriteGate.ValidateSlug"/> first (route slug format 400 → shipped-slug 409) — see
/// this route's own <paramref name="catalogSlug"/> checks immediately below, which run BETWEEN the two
/// phases — then <see cref="ThemeWriteGate.ReadParseAndValidateAsync"/> (body read 413 → schema-major
/// 400 → deserialize-as-validation 400 → font law/ceiling 400). <see cref="ThemesSaveAsOwnController.SaveAsOwn"/>
/// runs the SAME two phases back to back (via <see cref="ThemeWriteGate.RunAsync"/>) — this route and
/// that one are the ONLY two <c>station.theme</c> write paths, and both share one gate implementation
/// rather than two hand-copied blocks a reviewer had to eyeball for byte-identity: a gate added to
/// <see cref="ThemeWriteGate"/> tomorrow is a compile-visible change both routes pick up, never a copy
/// one route's own edit could silently leave the other behind on. See that type's own remarks for the
/// full per-gate reasoning, including why every refusal body it builds — malformed/empty JSON included
/// — is byte-identical between the two routes for the same input, and for the "TWO PHASES" reasoning
/// behind the split itself (review finding B2 — a single end-to-end call left this route's own
/// <paramref name="catalogSlug"/> check nowhere legal to run except BEHIND the body
/// read/parse/font-law gate, silently reordering an observable, pre-existing precedence: a bad
/// <c>catalogSlug</c> paired with an oversized body used to refuse 400 before the body was ever read;
/// folded into one call it refused 413 instead, and a bad <c>catalogSlug</c> paired with an
/// unvendored-font manifest started paying for a live <see cref="CatalogProxyService.GetIndexAsync"/>
/// round trip it never used to reach).
/// </para>
///
/// <para>
/// <b><paramref name="catalogSlug"/> format/length (400, F90.7 precedent) — the one gate
/// <see cref="ThemeWriteGate"/> does NOT own, run between its two phases.</b> Meaningless for
/// <see cref="ThemesSaveAsOwnController.SaveAsOwn"/> (a save carries no catalog-sourced slug at all),
/// so it stays here — checked AFTER <see cref="ThemeWriteGate.ValidateSlug"/> (the route slug itself
/// must still be well-formed and unreserved before anything about a DIFFERENT slug is even looked at —
/// the original, pre-<see cref="ThemeWriteGate"/> precedence) and BEFORE
/// <see cref="ThemeWriteGate.ReadParseAndValidateAsync"/> (a pure-string, no-I/O check has no business
/// sitting behind a bounded body read, a JSON parse, and a possible outbound catalog fetch — review
/// finding B2, pinned by name in <c>Story272_ThemeImport.cs</c>'s own <c>ScenarioCatalogSlugPrecedence</c>).
/// </para>
///
/// <para>
/// <b>Slug is the upsert key, not the manifest's own opinion (mirrors F79's persona precedent).</b>
/// <see cref="Core.Domain.Persona"/>'s import never has a mismatch to resolve — a
/// <see cref="Core.Domain.PersonaCard"/> carries no slug of its own, so the route slug always governs
/// silently. <see cref="ThemeManifest"/> DOES carry its own <see cref="ThemeManifest.Slug"/>, so this
/// route makes the same "route slug always governs" rule explicit — <see cref="ThemeWriteGate.RunAsync"/>
/// re-stamps the manifest with the route <paramref name="slug"/> before ever returning it. Without this,
/// an owner POSTing to <c>/api/themes/midnight-drive/import</c> with a manifest whose own <c>"slug"</c>
/// field reads <c>"old-name"</c> would store under <c>station.theme.slug = "midnight-drive"</c> while
/// <c>ThemeCatalog</c> re-parses <c>definition</c> and indexes the result under <c>"old-name"</c>
/// instead — the exact split-identity bug this normalization exists to make impossible.
/// </para>
///
/// <para>
/// <b>409 is ONLY the shipped-slug collision (SPEC F103.8).</b> <see cref="IThemeStore.UpsertAsync"/>
/// has no failure mode of its own (a plain upsert, unlike
/// <see cref="IPersonaImportStore.ImportAsync"/>'s name-uniqueness conflict) — station.theme's only
/// uniqueness constraint is the upsert key itself. A re-import of an existing OWNER slug is therefore
/// always a plain update, never a conflict — the shipped-slug check <see cref="ThemeWriteGate.RunAsync"/>
/// runs is the one real refusal (PLAN T184 review F3 — against the DI'd <see cref="themeCatalog"/>
/// singleton's own fixed, construction-time shipped set, never its current, possibly owner-widened one,
/// so an EARLIER owner import still can never block a later, unrelated one).
/// </para>
///
/// <para>
/// <b>Rebuild after write (SPEC F103.7) — with <see cref="CancellationToken.None"/>, deliberately
/// (PLAN T184 review F1).</b> <see cref="ThemeCatalog.ReloadOwnerThemesAsync"/> runs once more, on the
/// SAME DI'd singleton <see cref="ThemeCatalogOwnerLoadHostedService"/> warms at boot — the only way an
/// import reaches every already-running request handler (theme.css, the settings surface) with no
/// process restart. Runs only on the success path: a rejected import must change nothing, including
/// catalog state. By the time this call is reached, <see cref="IThemeStore.UpsertAsync"/> has already
/// committed — the write is no longer this request's to abandon. Passing the request's own
/// <paramref name="ct"/> here would let a client disconnecting mid-rebuild cancel it: the catch in
/// <see cref="ThemeCatalog.ReloadOwnerThemesAsync"/> swallows that as an ordinary reload failure and
/// falls back to the shipped-only <c>state</c> (SPEC F102.7's offline floor, correctly triggered for a
/// REAL owner-load fault) — collapsing every previously imported theme out of the running catalog
/// until the next successful import or restart, for a client that merely stopped listening rather than
/// anything actually wrong with <c>station.theme</c>. <see cref="CancellationToken.None"/> makes the
/// rebuild run to completion regardless of who is still connected, which is what a committed write
/// demands; <see cref="IThemeStore.UpsertAsync"/> above still takes <paramref name="ct"/> — an abort
/// BEFORE the write commits is exactly the write's own to honour.
/// </para>
/// </summary>
[ApiController]
[Route("api/themes")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed partial class ThemesImportController(
    IThemeStore themeStore,
    ThemeCatalog themeCatalog,
    InstalledFontCatalog installedFontCatalog,
    CatalogProxyService catalogProxyService,
    ILogger<ThemesImportController> logger) : ControllerBase
{
    /// <summary>
    /// POST /api/themes/{slug}/import — see this controller's own remarks for the full gate order and
    /// the reasoning behind each one. Gate order: <see cref="ThemeWriteGate.ValidateSlug"/> (slug
    /// format 400 → shipped-slug 409) → <paramref name="catalogSlug"/> format/length (400, F90.7
    /// precedent — the one gate this route alone runs, between the shared pipeline's two phases, PLAN
    /// T207 review finding B2) → <see cref="ThemeWriteGate.ReadParseAndValidateAsync"/> (body read 413
    /// → schema-major 400 → deserialize-as-validation 400 → font law/ceiling 400) → upsert + catalog
    /// rebuild (F103.7).
    /// </summary>
    [HttpPost("{slug}/import")]
    [Consumes("application/json")]
    [RequestSizeLimit(BoundedImportBodyReader.MaxImportBytes)]
    public async Task<IActionResult> Import(string slug, [FromQuery] string? catalogSlug, CancellationToken ct)
    {
        if (ThemeWriteGate.ValidateSlug(slug, themeCatalog) is { } slugRefusal)
            return slugRefusal;

        // Cheap, pure-string, no-I/O — deliberately BETWEEN ThemeWriteGate's two phases, never after
        // ReadParseAndValidateAsync (review finding B2's own "must not sit behind a body read + parse +
        // potential outbound call"): the original, pre-ThemeWriteGate precedence.
        if (!string.IsNullOrEmpty(catalogSlug))
        {
            if (catalogSlug.Length > BoundedImportBodyReader.MaxCatalogSlugLength)
                return BadRequest(CatalogSlugTooLongProblem(catalogSlug.Length));

            if (!SlugFormat().IsMatch(catalogSlug))
                return BadRequest(BadCatalogSlugProblem(catalogSlug));
        }

        var (refusal, manifestOrNull) = await ThemeWriteGate.ReadParseAndValidateAsync(
            Request, slug, installedFontCatalog, catalogProxyService, ct);
        if (refusal is not null)
            return refusal;
        if (manifestOrNull is not { } normalized)
            throw new UnreachableException($"{nameof(ThemeWriteGate)}.{nameof(ThemeWriteGate.ReadParseAndValidateAsync)} returned neither a refusal nor a manifest.");

        var importedFrom = string.IsNullOrEmpty(catalogSlug) ? "file" : catalogSlug;

        await themeStore.UpsertAsync(slug, ThemeManifestSerializer.Serialize(normalized), importedFrom, ct);

        // CancellationToken.None, deliberately — see this controller's own remarks ("Rebuild after
        // write") for why: the write above has already committed, so the rebuild is no longer this
        // request's to abandon.
        await themeCatalog.ReloadOwnerThemesAsync(CancellationToken.None);

        // Both values are \A..\z-anchored long before this line, so no control character can
        // reach the template — Sanitize is the belt-and-braces LogSafeText's own rule demands
        // ("every string in a catalog log line"), and what CodeQL's log-forging query wants pinned.
        logger.LogInformation(
            "Theme imported slug={Slug} importedFrom={ImportedFrom}",
            LogSafeText.Sanitize(slug),
            LogSafeText.Sanitize(importedFrom));

        // gh-#375 (gh-#375, ThemeImportResponse's own remarks) — a read-back, not a client-side
        // DateTime.UtcNow guess: the store just stamped imported_at unconditionally (both the
        // insert and the update ON CONFLICT branch, see IThemeStore.UpsertAsync's own remarks) with
        // an importedFrom that is NEVER null on this path, so OwnerTheme's own "ImportedAt is null
        // exactly when ImportedFrom is" invariant guarantees a value here — a null read-back would
        // mean the write this request just awaited never actually committed, worth surfacing loudly
        // rather than papering over with a fabricated timestamp the admin UI would show as fact.
        var stored = await themeStore.GetBySlugAsync(slug, ct)
            ?? throw new InvalidOperationException($"Theme '{slug}' was upserted but could not be read back.");
        var importedAt = stored.ImportedAt
            ?? throw new InvalidOperationException($"Theme '{slug}' was imported but carries no imported_at stamp.");

        return Ok(new ThemeImportResponse(normalized.Slug, normalized.Name, importedFrom, importedAt));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Composed from CatalogIndexValidator.SlugSegment — the catalog's own slug vocabulary — mirroring
    // CatalogController.SlugFormat's own composition (see that member's remarks for the full \A/\z
    // anchoring rationale: .NET's `$` matches before a trailing '\n', which a naive ^/$ pattern would
    // let slip through as e.g. "midnight-drive\n"). Used ONLY for catalogSlug validation now that
    // ThemeWriteGate.ValidateSlug owns the route-slug check (PLAN T207 review finding F1) — a private
    // copy rather than a call to that type's own (internal, differently-scoped) slug regex, since the
    // two checks validate two different strings for two different reasons.
    [GeneratedRegex(@"\A" + CatalogIndexValidator.SlugSegment + @"\z")]
    private static partial Regex SlugFormat();

    static ProblemDetails BadCatalogSlugProblem(string catalogSlug) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid catalog slug.",
        Detail =
            $"\"{catalogSlug}\" is not a valid catalog slug (lowercase letters, digits, and single hyphens only).",
    };

    static ProblemDetails CatalogSlugTooLongProblem(int length) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid catalog slug.",
        Detail = $"catalogSlug must be at most {BoundedImportBodyReader.MaxCatalogSlugLength} characters (got {length}).",
    };
}
