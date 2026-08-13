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
    /// Upserts by <paramref name="slug"/> (SPEC F103.6/F103.7/F104.13): a new slug inserts a row, an
    /// existing one replaces its <paramref name="definition"/> and refreshes
    /// <paramref name="importedFrom"/>/<c>imported_at</c> unconditionally — mirrors
    /// <see cref="IPersonaImportStore.ImportAsync"/>'s own "a re-import refreshes the stamp" rule.
    /// <c>imported_at</c> is the write's own <c>now()</c> whenever <paramref name="importedFrom"/> is
    /// non-null, and <see langword="null"/> whenever <paramref name="importedFrom"/> itself is
    /// (PLAN T207's save-as-own write, SPEC F104.13's reserved authored-provenance value) — see
    /// <see cref="OwnerTheme"/>'s own "<c>ImportedAt</c> is <see langword="null"/> exactly when
    /// <c>ImportedFrom</c> is" invariant, which every implementation of this method must honour.
    /// </summary>
    Task UpsertAsync(string slug, string definition, string? importedFrom, CancellationToken ct);

    /// <summary>
    /// The save-as-own write's own ATOMIC conditional upsert (SPEC F104.13, PLAN T207 review finding
    /// F2, gh-#394): always writes <c>imported_from</c>/<c>imported_at</c> as <see langword="null"/> —
    /// the one provenance value a save-as-own row is ever stamped with — and only overwrites an
    /// EXISTING row when that row's own <c>imported_from</c> is already <see langword="null"/> (a fresh
    /// slug, or a slug this same write path authored before). Returns <see langword="false"/>, never
    /// throws, when a conflicting row already holds real (non-null) <c>imported_from</c> provenance —
    /// the caller MUST check the return value and map <see langword="false"/> to its own refusal (SPEC
    /// F104.13's fail-closed overwrite ruling; <see cref="GenWave.Host.Api.ThemesSaveAsOwnController"/>
    /// maps it to <c>SlugHoldsAnImportedTheme</c>'s 409).
    ///
    /// <para>
    /// Deliberately its own method, not a branch folded into <see cref="UpsertAsync"/> keyed off a null
    /// <paramref name="importedFrom"/> in that call — <see cref="UpsertAsync"/>'s only caller (the
    /// import route) has no guard to enforce at all (a re-import of an existing owner slug is always a
    /// plain update, never a conflict, that route's own remarks), so smuggling a conditional WHERE into
    /// its shared SQL would gate a write path that was never meant to be gated. Mirrors
    /// <c>ShowRepository</c>'s own <c>CreateAsync</c>/<c>UpdateAsync</c>-vs-<c>ImportAsync</c> split for
    /// the identical reason: a write path's own guard belongs in its own method.
    /// </para>
    /// </summary>
    Task<bool> SaveAsOwnAsync(string slug, string definition, CancellationToken ct);

    /// <summary>Every owner theme row, in no particular guaranteed order — <c>ThemeCatalog</c>
    /// (T182) folds these into its own load-order list alongside the two embedded manifests.</summary>
    Task<IReadOnlyList<OwnerTheme>> GetAllAsync(CancellationToken ct);

    /// <summary>The owner theme identified by <paramref name="slug"/>, or <see langword="null"/> if
    /// no such row exists.</summary>
    Task<OwnerTheme?> GetBySlugAsync(string slug, CancellationToken ct);
}
