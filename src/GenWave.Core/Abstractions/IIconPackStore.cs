using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F130, STORY-337, PLAN T290) — persistence for Dean-curated icon packs installed from the
/// Community Catalog's <c>icon</c> kind, in <c>station.icon_pack</c>. Ships dark: no consumer lands
/// with this seam yet — <c>POST /api/icon-packs/{slug}/install</c> (T303) is the first write consumer,
/// <c>IconPackRenderer</c> and the <c>Station:IconPack</c> settings dropdown (T303+) the first read
/// consumers. Mirrors <see cref="IThemeStore"/>'s own shape almost exactly (a single jsonb-backed table,
/// no child rows) with <see cref="IFontPackStore"/>'s own non-nullable <c>imported_from</c> (a pack has
/// no authored-in-place path).
/// </summary>
public interface IIconPackStore
{
    /// <summary>
    /// Upserts by <paramref name="slug"/>: a new slug inserts a row, an existing one replaces
    /// <paramref name="definition"/> and refreshes <paramref name="importedFrom"/>/<c>imported_at</c>
    /// unconditionally — mirrors <see cref="IThemeStore.UpsertAsync"/>'s own "a re-import refreshes the
    /// stamp" rule, minus that method's own nullable-<c>importedFrom</c> authored-in-place branch (an
    /// icon pack has none).
    /// </summary>
    Task UpsertAsync(string slug, string definition, string importedFrom, CancellationToken ct);

    /// <summary>The installed pack identified by <paramref name="slug"/>, or <see langword="null"/> if
    /// no such row exists.</summary>
    Task<IconPack?> GetBySlugAsync(string slug, CancellationToken ct);

    /// <summary>Every installed pack, in no particular guaranteed order.</summary>
    Task<IReadOnlyList<IconPack>> GetAllAsync(CancellationToken ct);

    /// <summary>Removes the pack identified by <paramref name="slug"/>. Returns <see langword="true"/>
    /// when a pack was deleted, <see langword="false"/> when no such pack existed. No referenced-by
    /// guard — <c>Station:IconPack</c> names an installed pack by slug in the settings overlay, never a
    /// structural FK into this table, so uninstalling a pack currently selected simply leaves that
    /// setting dangling for the live-setting reader to fall back on (T303's own concern, not this
    /// store's).</summary>
    Task<bool> DeleteAsync(string slug, CancellationToken ct);
}
