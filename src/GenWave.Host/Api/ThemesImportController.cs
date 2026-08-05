using System.Text.Json;
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
/// own running-total read (see that method's remarks — a shared helper both this controller and
/// <see cref="PersonaController.Import"/> call, PLAN T184 review F4; the two used to carry a verbatim
/// copy each, a duplicated security control), same slug-format gate (here composed from
/// <see cref="CatalogIndexValidator.SlugSegment"/> — the catalog's own slug vocabulary, since this
/// route is catalog-adjacent rather than persona-adjacent; see <see cref="CatalogController.SlugFormat"/>'s
/// own remarks for why that composition, not a private copy, is the house idiom), and the same
/// deserialization-IS-validation posture: a <see cref="ThemeManifestException"/> from
/// <see cref="ThemeManifestParser.Parse"/> (malformed JSON, or any structural defect the parser's own
/// load-time rules catch — missing slug/name/author, an incomplete mode pair, an unsafe token/font
/// shape) maps to 400, never an unhandled 500.
/// </para>
///
/// <para>
/// <b>Two parses, by design (PLAN T184 review F5).</b> The body is parsed TWICE, not once: first as a
/// bare <see cref="JsonDocument"/> so <see cref="ExtractSchemaVersion"/> can read the optional
/// <c>schemaVersion</c> field cheaply, then — only once that gate has passed — handed unchanged to
/// <see cref="ThemeManifestParser.Parse"/> for the real structural validation. The order is the point:
/// a v2-shaped manifest whose shape ALSO fails today's v1 parser (a newer major is free to look
/// nothing like the current one) must fail with the version-naming message, never a misleading
/// "missing mode" or similar structural complaint from a parser it was never going to satisfy. A
/// syntactically malformed body still reports through <see cref="ThemeManifestParser.Parse"/>'s own
/// message — the first, schema-version-only parse catches and defers to it rather than duplicating
/// that message itself.
/// </para>
///
/// <para>
/// <b>Schema-major reject (SPEC F103.6, STORY-272 AC6) — a deliberate, additive gate.</b> Unlike
/// <see cref="Core.Domain.PersonaCard"/>, <see cref="ThemeManifest"/> carries no
/// <c>SchemaVersion</c> field today — the format has had exactly one shape since T156, and adding one
/// would touch the byte-stable interchange contract T177/T178 pinned across this repo and
/// <c>genwave-catalog</c> (<c>Fixtures/golden.theme.json</c>), which is out of this task's scope. AC6
/// still needs a real gate, so this route reads an OPTIONAL <c>schemaVersion</c> integer straight off
/// the raw request JSON (<see cref="ExtractSchemaVersion"/>) — three outcomes, not two (PLAN T184
/// review F2): ABSENT (every manifest that exists today, shipped or fixture) ⇒ treated as version 1
/// and passes, at zero cost to any current caller; PRESENT and over <see cref="CurrentSchemaVersion"/>
/// ⇒ refused, naming both; PRESENT but not a readable <see cref="int"/> — a JSON string, a fractional
/// number, an integer that overflows — ⇒ ALSO refused, rather than silently coerced to "absent". Only
/// true absence gets <see cref="Core.Domain.PersonaCard"/>'s own forward-compat treatment ("unknown
/// fields within the current major are silently tolerated"); a present-but-unparsable value is an
/// operator/tooling error worth surfacing, not one to paper over as version 1.
/// </para>
///
/// <para>
/// <b>Slug is the upsert key, not the manifest's own opinion (mirrors F79's persona precedent).</b>
/// <see cref="Core.Domain.Persona"/>'s import never has a mismatch to resolve — a
/// <see cref="Core.Domain.PersonaCard"/> carries no slug of its own, so the route slug always governs
/// silently. <see cref="ThemeManifest"/> DOES carry its own <see cref="ThemeManifest.Slug"/>, so this
/// route makes the same "route slug always governs" rule explicit: after a successful parse, the
/// manifest is re-stamped with the route <paramref name="slug"/> (<see cref="NormalizeSlug"/>) before
/// it is ever serialized for storage. Without this, an owner POSTing to
/// <c>/api/themes/midnight-drive/import</c> with a manifest whose own <c>"slug"</c> field reads
/// <c>"old-name"</c> would store under <c>station.theme.slug = "midnight-drive"</c> while
/// <c>ThemeCatalog</c> re-parses <c>definition</c> and indexes the result under
/// <c>"old-name"</c> instead — the exact split-identity bug this normalization exists to make
/// impossible.
/// </para>
///
/// <para>
/// <b>409 is ONLY the shipped-slug collision (SPEC F103.8).</b> <see cref="IThemeStore.UpsertAsync"/>
/// has no failure mode of its own (a plain upsert, unlike
/// <see cref="IPersonaImportStore.ImportAsync"/>'s name-uniqueness conflict) — station.theme's only
/// uniqueness constraint is the upsert key itself. A re-import of an existing OWNER slug is therefore
/// always a plain update, never a conflict. The one real refusal is checked here, against
/// <see cref="ThemeCatalog.IsShippedSlug"/> on the DI'd <see cref="themeCatalog"/> singleton (PLAN
/// T184 review F3 — this used to parse a fresh, throwaway <see cref="ThemeCatalog.LoadShipped"/> per
/// request instead; <see cref="ThemeCatalog.IsShippedSlug"/> reads that same instance's own fixed,
/// construction-time shipped set, never its current, possibly owner-widened one, so an EARLIER owner
/// import still can never block a later, unrelated one — SPEC F103.8's own "reserve whatever
/// <c>LoadShipped</c> enumerates" contract, so a future shipped-set resize (T191's 6→2 split) changes
/// what is reserved with no edit here, and the one rule now lives in exactly one place: see
/// <see cref="ThemeCatalog.IsShippedSlug"/>'s own remarks). Checked before the body is even read: it
/// depends on nothing but the already-validated route slug, so a doomed request fails as cheaply as
/// the slug-format gate above it.
/// </para>
///
/// <para>
/// <b>Curated-font provenance/byte-ceiling (SPEC F103.10, PLAN T188) — the one place it is
/// enforced.</b> After <see cref="ThemeManifestParser.Parse"/> succeeds, the parsed manifest is
/// checked against <see cref="ThemeFontProvenanceValidator"/>: every font asset it references must
/// resolve to a face in <see cref="FontProvenanceCatalog.Default"/> (the GenWave-vendored curated
/// set), and its distinct referenced faces' summed bytes must clear FONTS.md's per-theme ceiling. A
/// failure maps to 400, same posture as a structural manifest defect. This route is the ONLY
/// <c>station.theme</c> write path, so passing this gate here is what guarantees every row this
/// store ever holds already satisfies SPEC F103.10 — see <see cref="ThemeFontProvenanceValidator"/>'s
/// own remarks for why <see cref="ThemeCatalog.ReloadOwnerThemesAsync"/> needs no second check.
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
    ILogger<ThemesImportController> logger) : ControllerBase
{
    // See this controller's own remarks ("Schema-major reject") for why this lives here rather than
    // on ThemeManifest itself.
    const int CurrentSchemaVersion = 1;

    /// <summary>
    /// POST /api/themes/{slug}/import — see this controller's own remarks for the full gate order and
    /// the reasoning behind each one. Gate order: route slug format (400) → shipped-slug reservation
    /// (409, F103.8) → <paramref name="catalogSlug"/> format (400, F90.7 precedent) → bounded body
    /// read (413) → schema-major (400, AC6, checked ahead of structural parsing — PLAN T184 review F5)
    /// → deserialize-as-validation (400, F103.6) → curated-font provenance/byte-ceiling (400, F103.10,
    /// PLAN T188 — <see cref="ThemeFontProvenanceValidator"/>, the only <c>station.theme</c> write
    /// path, so a row is never stored referencing an unvendored face or an over-budget font set) →
    /// upsert + catalog rebuild (F103.7).
    /// </summary>
    [HttpPost("{slug}/import")]
    [Consumes("application/json")]
    [RequestSizeLimit(BoundedImportBodyReader.MaxImportBytes)]
    public async Task<IActionResult> Import(string slug, [FromQuery] string? catalogSlug, CancellationToken ct)
    {
        if (!SlugFormat().IsMatch(slug))
            return BadRequest(BadSlugProblem(slug));

        if (themeCatalog.IsShippedSlug(slug))
            return Conflict(ShippedSlugReservedProblem(slug));

        if (!string.IsNullOrEmpty(catalogSlug))
        {
            if (catalogSlug.Length > BoundedImportBodyReader.MaxCatalogSlugLength)
                return BadRequest(CatalogSlugTooLongProblem(catalogSlug.Length));

            if (!SlugFormat().IsMatch(catalogSlug))
                return BadRequest(BadCatalogSlugProblem(catalogSlug));
        }

        var (json, oversized) = await BoundedImportBodyReader.ReadBoundedBodyAsync(
            Request, BoundedImportBodyReader.MaxImportBytes, ct);
        if (oversized)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, ImportProblems.Oversized());

        // See this controller's own remarks ("Two parses, by design") — the schema-version gate below
        // reads a bare JsonDocument BEFORE ThemeManifestParser.Parse ever sees the body, so a
        // version-mismatched manifest is refused naming both versions even when its shape would also
        // fail structural parsing. A syntactically malformed body is deliberately NOT reported here —
        // ThemeManifestParser.Parse below throws the one, well-formed message for that.
        int? schemaVersion = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var (version, unreadable) = ExtractSchemaVersion(document.RootElement);
            if (unreadable)
                return BadRequest(UnreadableSchemaVersionProblem());

            schemaVersion = version;
        }
        catch (JsonException)
        {
            // Malformed JSON — deferred to ThemeManifestParser.Parse below, which throws the same
            // failure as a ThemeManifestException carrying its own well-formed message.
        }

        if (schemaVersion is { } manifestSchemaVersion && manifestSchemaVersion > CurrentSchemaVersion)
            return BadRequest(NewerSchemaProblem(manifestSchemaVersion));

        ThemeManifest manifest;
        try
        {
            manifest = ThemeManifestParser.Parse(new ThemeManifestSource($"import:{slug}", json));
        }
        catch (ThemeManifestException ex)
        {
            return BadRequest(ImportProblems.MalformedManifest(ex.Message));
        }

        // SPEC F103.10, PLAN T188 — this route is the ONLY station.theme write path, so this is the
        // one gate that must hold for every row ever persisted; see ThemeFontProvenanceValidator's
        // own "Placement" remarks for why ThemeCatalog.ReloadOwnerThemesAsync needs no second check.
        try
        {
            ThemeFontProvenanceValidator.Validate(
                manifest, FontProvenanceCatalog.Default.BySrc, ThemeFontProvenanceValidator.PerThemeByteCeilingBytes);
        }
        catch (ThemeManifestException ex)
        {
            return BadRequest(UnvendoredFontProblem(ex.Message));
        }

        var normalized = NormalizeSlug(manifest, slug);
        var importedFrom = string.IsNullOrEmpty(catalogSlug) ? "file" : catalogSlug;

        await themeStore.UpsertAsync(slug, ThemeManifestSerializer.Serialize(normalized), importedFrom, ct);

        // CancellationToken.None, deliberately — see this controller's own remarks ("Rebuild after
        // write") for why: the write above has already committed, so the rebuild is no longer this
        // request's to abandon.
        await themeCatalog.ReloadOwnerThemesAsync(CancellationToken.None);

        logger.LogInformation("Theme imported slug={Slug} importedFrom={ImportedFrom}", slug, importedFrom);

        return Ok(new ThemeImportResponse(normalized.Slug, normalized.Name, importedFrom));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Re-stamps <paramref name="manifest"/>'s own <see cref="ThemeManifest.Slug"/> to the
    /// route <paramref name="routeSlug"/> — see this controller's own remarks ("Slug is the upsert
    /// key, not the manifest's own opinion") for why.</summary>
    static ThemeManifest NormalizeSlug(ThemeManifest manifest, string routeSlug) =>
        manifest with { Slug = routeSlug };

    /// <summary>
    /// Reads the optional top-level <c>schemaVersion</c> field off <paramref name="root"/> — see this
    /// controller's own remarks ("Schema-major reject") for why this is not a
    /// <see cref="ThemeManifest"/> field. Three outcomes, not two (PLAN T184 review F2): the field is
    /// ABSENT ⇒ <c>(null, false)</c>, treated by <see cref="Import"/> as version
    /// <see cref="CurrentSchemaVersion"/>; PRESENT and a readable <see cref="int"/> ⇒
    /// <c>(version, false)</c>; PRESENT but not a readable <see cref="int"/> — a JSON string, a
    /// fractional number, or one that overflows <see cref="int"/> — ⇒ <c>(null, true)</c>, a refusal
    /// rather than the silent "treat as absent" this method used to fail open to. Guards
    /// <paramref name="root"/>'s own <see cref="JsonElement.ValueKind"/> before calling
    /// <see cref="JsonElement.TryGetProperty(string,out JsonElement)"/>, which throws for a
    /// syntactically valid but non-object root (a bare JSON array/string/number) — that shape is
    /// reported by <see cref="ThemeManifestParser.Parse"/> instead, never here.
    /// </summary>
    static (int? Version, bool Unreadable) ExtractSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var property))
            return (null, false);

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var version)
            ? (version, false)
            : (null, true);
    }

    // Composed from CatalogIndexValidator.SlugSegment — the catalog's own slug vocabulary — mirroring
    // CatalogController.SlugFormat's own composition (see that member's remarks for the full \A/\z
    // anchoring rationale: .NET's `$` matches before a trailing '\n', which a naive ^/$ pattern would
    // let slip through as e.g. "midnight-drive\n").
    [GeneratedRegex(@"\A" + CatalogIndexValidator.SlugSegment + @"\z")]
    private static partial Regex SlugFormat();

    static ProblemDetails BadSlugProblem(string slug) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid slug.",
        Detail = $"\"{slug}\" is not a valid theme slug (lowercase letters, digits, and single hyphens only).",
    };

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

    static ProblemDetails NewerSchemaProblem(int manifestSchemaVersion) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unsupported schema version.",
        Detail =
            $"Theme manifest schema version {manifestSchemaVersion} is newer than this station's " +
            $"supported version {CurrentSchemaVersion}.",
    };

    static ProblemDetails UnreadableSchemaVersionProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid schema version.",
        Detail = "schemaVersion, when present, must be a whole number.",
    };

    static ProblemDetails ShippedSlugReservedProblem(string slug) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Shipped theme slug is reserved.",
        Detail = $"\"{slug}\" is a shipped theme's slug and cannot be overwritten by an import (SPEC F103.8).",
    };

    /// <summary><paramref name="detail"/> carries <see cref="ThemeFontProvenanceValidator.Validate"/>'s
    /// own <see cref="ThemeManifestException"/> message verbatim — either an unvendored-face name (and
    /// the whole vendored set) or an over-ceiling byte total (SPEC F103.10, PLAN T188).</summary>
    static ProblemDetails UnvendoredFontProblem(string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Theme fonts rejected.",
        Detail = detail,
    };
}
