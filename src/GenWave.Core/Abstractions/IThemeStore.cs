using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F103.7, F103.8; STORY-271, PLAN T181) — persistence for owner-imported themes in
/// <c>station.theme</c> (the Community Catalog v2 theme kind). Ships dark: no consumer lands with
/// this seam yet. <c>ThemeCatalog</c> (T182) is the first read consumer — it loads every
/// <see cref="OwnerTheme"/> row alongside the two embedded manifests through the same
/// <c>Load</c>/<c>ThemeManifestParser</c> path; <c>POST /api/themes/{slug}/import</c> (T184) is the
/// first write consumer — it serializes an accepted <c>ThemeManifest</c> via
/// <c>ThemeManifestSerializer</c> and upserts the result here. Deliberately dealing in the raw
/// serialized <c>definition</c> (a string) rather than <c>GenWave.Host.Theming.ThemeManifest</c>
/// itself — that type lives in <c>GenWave.Host</c>, downstream of this <c>GenWave.Core</c> seam, so
/// every caller (de)serializes at its own edge; see <see cref="OwnerTheme"/>'s own remarks.
///
/// This seam is NOT responsible for SPEC F103.8's "a shipped default's slug is reserved" guard —
/// that check needs the embedded/shipped catalog, which this store knows nothing about, so it
/// belongs to whichever caller already holds a <c>ThemeCatalog</c> (T182's import/resolve path).
/// </summary>
public interface IThemeStore
{
    /// <summary>
    /// Upserts by <paramref name="slug"/> (SPEC F103.6/F103.7): a new slug inserts a row, an
    /// existing one replaces its <paramref name="definition"/> and refreshes
    /// <paramref name="importedFrom"/>/<c>imported_at</c> unconditionally — mirrors
    /// <see cref="IPersonaImportStore.ImportAsync"/>'s own "a re-import refreshes the stamp" rule.
    /// <c>imported_at</c> is always the write's own <c>now()</c>.
    /// </summary>
    Task UpsertAsync(string slug, string definition, string? importedFrom, CancellationToken ct);

    /// <summary>Every owner theme row, in no particular guaranteed order — <c>ThemeCatalog</c>
    /// (T182) folds these into its own load-order list alongside the two embedded manifests.</summary>
    Task<IReadOnlyList<OwnerTheme>> GetAllAsync(CancellationToken ct);

    /// <summary>The owner theme identified by <paramref name="slug"/>, or <see langword="null"/> if
    /// no such row exists.</summary>
    Task<OwnerTheme?> GetBySlugAsync(string slug, CancellationToken ct);
}
