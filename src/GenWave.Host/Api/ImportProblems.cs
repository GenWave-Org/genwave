using Microsoft.AspNetCore.Mvc;

namespace GenWave.Host.Api;

/// <summary>
/// Shared <see cref="ProblemDetails"/> factories for the portable-JSON theme routes (SPEC F79.6,
/// F103.5, F103.6, F103.8, F103.10, F104.13; <see cref="ThemesImportController"/>,
/// <see cref="ThemePreviewController"/>, <see cref="ThemesSaveAsOwnController"/>) — the problem shapes
/// these controllers' slug-format, shipped-slug, size-cap, malformed-manifest, schema-major, and
/// font-provenance gates return were a verbatim-duplicated copy each (including the size-cap copy
/// string, review finding T186) until this type. Lives next to <see cref="BoundedImportBodyReader"/>
/// and <see cref="ThemeSchemaVersionGate"/>, the shared CONTROLS these factories describe the failure
/// of — same "shared control, one home" idiom those types' own remarks already established (PLAN T184
/// review F4; carried forward to the schema-major and font-provenance gates when the preview route
/// grew them too, Dean's directive 2026-08-05; carried forward again to the slug-format/shipped-slug
/// gates once <see cref="ThemesSaveAsOwnController"/> became <c>station.theme</c>'s second write route,
/// PLAN T207 review carry-in 2).
/// </summary>
internal static class ImportProblems
{
    /// <summary>The shared 413 body both routes return when
    /// <see cref="BoundedImportBodyReader.ReadBoundedBodyAsync"/> reports oversized.</summary>
    public static ProblemDetails Oversized() => new()
    {
        Status = StatusCodes.Status413PayloadTooLarge,
        Title  = "Payload too large.",
        Detail = $"Theme manifests are capped at {BoundedImportBodyReader.MaxImportBytes / 1024} KB.",
    };

    /// <summary>The shared 400 body both routes return when
    /// <see cref="ThemeManifestParser.Parse"/> throws a <see cref="ThemeManifestException"/> —
    /// <paramref name="detail"/> carries that exception's own message verbatim.</summary>
    public static ProblemDetails MalformedManifest(string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Malformed theme manifest.",
        Detail = detail,
    };

    /// <summary>The shared 400 body every <c>station.theme</c> write route returns when its own route
    /// <paramref name="slug"/> fails <see cref="ThemesImportController"/>'s slug-format gate (lifted
    /// here at PLAN T207 review carry-in 2, alongside <see cref="ShippedSlugReserved"/>, once
    /// <see cref="ThemesSaveAsOwnController"/> became this route's second caller — STORY-287 AC3's
    /// "same copy" demands the identical text, not a hand-copied second literal).</summary>
    public static ProblemDetails BadThemeSlug(string slug) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid slug.",
        Detail = $"\"{slug}\" is not a valid theme slug (lowercase letters, digits, and single hyphens only).",
    };

    /// <summary>The shared 409 body every <c>station.theme</c> write route returns when its own route
    /// <paramref name="slug"/> collides with a shipped theme's own slug (SPEC F103.8) — lifted here at
    /// PLAN T207 review carry-in 2 for the same "same copy" reason as <see cref="BadThemeSlug"/>.</summary>
    public static ProblemDetails ShippedSlugReserved(string slug) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Shipped theme slug is reserved.",
        Detail = $"\"{slug}\" is a shipped theme's slug and cannot be overwritten (SPEC F103.8).",
    };

    /// <summary>The shared 400 body both routes return when
    /// <see cref="ThemeSchemaVersionGate.ExtractSchemaVersion"/> finds a <c>schemaVersion</c> over
    /// <see cref="ThemeSchemaVersionGate.CurrentSchemaVersion"/> — naming both versions verbatim (SPEC
    /// F103.6 AC6), the exact phrase both routes' specs assert.</summary>
    public static ProblemDetails NewerSchema(int manifestSchemaVersion) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unsupported schema version.",
        Detail =
            $"Theme manifest schema version {manifestSchemaVersion} is newer than this station's " +
            $"supported version {ThemeSchemaVersionGate.CurrentSchemaVersion}.",
    };

    /// <summary>The shared 400 body both routes return when
    /// <see cref="ThemeSchemaVersionGate.ExtractSchemaVersion"/> reports a present-but-unparsable
    /// <c>schemaVersion</c> (a JSON string, a fraction, an overflowing integer).</summary>
    public static ProblemDetails UnreadableSchemaVersion() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid schema version.",
        Detail = "schemaVersion, when present, must be a whole number.",
    };

    /// <summary>The shared 400 body both routes return when
    /// <see cref="ThemeFontProvenanceValidator.Validate"/> throws (SPEC F103.10, PLAN T188) —
    /// <paramref name="detail"/> carries that exception's own message verbatim (SPEC F104.9's widened
    /// law, PLAN T205, still throws through the SAME exception), either an unvendored/uninstalled-face
    /// name (and the whole vendored set) or an over-ceiling byte total, optionally already run through
    /// <see cref="UnvendoredFontDetail"/> below.</summary>
    public static ProblemDetails UnvendoredFont(string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Theme fonts rejected.",
        Detail = detail,
    };

    /// <summary>The 409 body <see cref="ThemesSaveAsOwnController.SaveAsOwn"/> alone returns when its
    /// own route <paramref name="slug"/> already holds a theme IMPORTED from elsewhere (a non-null
    /// <c>imported_from</c>) — SPEC F104.13's fail-closed overwrite ruling (Dean's standing fail-closed
    /// preference, PLAN T207 review finding F2): an authored save must never silently destroy another
    /// theme's own imported provenance. Route-specific, unlike every other factory in this type —
    /// <see cref="ThemesImportController.Import"/> has no such refusal at all (a re-import of an
    /// existing owner slug is always a plain update, that class's own remarks); only save-as-own ever
    /// refuses on this condition.</summary>
    public static ProblemDetails SlugHoldsAnImportedTheme(string slug) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Slug holds an imported theme.",
        Detail =
            $"\"{slug}\" already holds a theme imported from elsewhere and cannot be overwritten by a " +
            "save — pick a different slug, or re-import to update it instead.",
    };

    /// <summary>
    /// SPEC F104.10, PLAN T205 — appends an "install pack…" suggestion for every missing font src the
    /// catalog index tells the caller a pack could provide, onto <paramref name="baseDetail"/>
    /// (<see cref="ThemeFontProvenanceValidator.Validate"/>'s own missing-face message). The ONE shared
    /// copy home both <c>station.theme</c> write routes — the theme import route (T205) and the
    /// save-as-own route (T207 — "the import route's exact copy… reused verbatim", STORY-287 AC3) —
    /// build their widened-font-law 400 detail from (via <see cref="FontPackSuggestionBuilder"/>, which
    /// calls this method), so the two routes can never silently drift onto two different phrasings for
    /// the identical refusal.
    ///
    /// <para>
    /// <paramref name="providingPackSlugsByMissingSrc"/> is EMPTY — never a caller's own inline string —
    /// whenever there is nothing to suggest: a ceiling-only refusal (nothing is actually missing), or a
    /// missing face the catalog index could not resolve a pack for (including because the index itself
    /// is unreachable — SPEC F104.10's own fail-soft posture: a missing face is ALWAYS named by
    /// <paramref name="baseDetail"/> already; the pack suggestion is best-effort, additive prose only,
    /// never a precondition for refusing).
    /// </para>
    /// </summary>
    public static string UnvendoredFontDetail(
        string baseDetail, IReadOnlyDictionary<string, string> providingPackSlugsByMissingSrc)
    {
        if (providingPackSlugsByMissingSrc.Count == 0)
            return baseDetail;

        var suggestions = providingPackSlugsByMissingSrc
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"\"{pair.Key}\" is provided by pack \"{pair.Value}\" — install it to make this face available.");
        return $"{baseDetail} {string.Join(" ", suggestions)}";
    }
}
