namespace GenWave.Core.Domain;

/// <summary>
/// One row read back from <c>station.theme</c> (SPEC F103.7, STORY-271, PLAN T181) — an
/// owner-imported theme (Community Catalog v2's theme kind). <see cref="Definition"/> is the raw
/// jsonb text: the byte-stable manifest <c>GenWave.Host.Theming.ThemeManifestSerializer</c>
/// produced at import time. Deliberately opaque here rather than typed as
/// <c>GenWave.Host.Theming.ThemeManifest</c> — that type lives in <c>GenWave.Host</c>, downstream of
/// this <c>GenWave.Core</c> seam, so a caller (<c>ThemeCatalog</c>, T182) reconstitutes it via
/// <c>ThemeManifestParser</c>/<c>ThemeManifestSource</c> at its own edge, exactly the way
/// <c>ThemeCatalog.LoadShipped</c> already turns a raw embedded-resource string into a
/// <c>ThemeManifestSource</c> before parsing.
/// </summary>
/// <param name="Slug">The manifest's own slug — unique across every owner theme (the table's
/// <c>UNIQUE(slug)</c> constraint).</param>
/// <param name="Definition">The stored <c>definition</c> column, verbatim jsonb text.</param>
/// <param name="ImportedFrom">Provenance stamp (SPEC F103.11, mirrors station.persona's db/25
/// precedent): the catalog entry's slug for a catalog import, <c>"file"</c> for a direct upload, or
/// <c>null</c> for an authored-in-place theme (no writer for that path exists yet).</param>
/// <param name="ImportedAt">The moment <see cref="ImportedFrom"/> was last stamped; <c>null</c>
/// exactly when <see cref="ImportedFrom"/> is <c>null</c>.</param>
/// <param name="CreatedAt">When this row was first inserted.</param>
public sealed record OwnerTheme(
    string Slug,
    string Definition,
    string? ImportedFrom,
    DateTime? ImportedAt,
    DateTime CreatedAt);
