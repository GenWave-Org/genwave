using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F104 "The wardrobe workshop"; STORY-282, PLAN T198) — persistence for Dean-curated
/// font packs installed from the Community Catalog's <c>font</c> kind, in
/// <c>station.font_pack</c>(+<c>_face</c>). Ships dark: no consumer lands with this seam yet —
/// <c>POST /api/fonts/{slug}/install</c> (T199) is the first write consumer, stamping
/// <c>imported_from</c> with the catalog entry's own slug (a pack has no authored-in-place path,
/// unlike <see cref="IThemeStore"/>'s theme rows); <c>InstalledFontCatalog</c> (T199/T200) and the
/// library page (T203) are the first read consumers of <see cref="GetAllAsync"/>; the widened
/// <c>/fonts/{file}</c> route (T200) is the first consumer of <see cref="GetFaceByFileAsync"/>.
/// Mirrors <see cref="IThemeStore"/>'s own "raw serialized definition string, never a Host-side
/// manifest type" discipline: <c>definition</c> stays opaque jsonb text here — this project knows
/// nothing of <c>GenWave.Host.Catalog.CatalogFontManifest</c> (downstream of this seam), so every
/// caller (de)serializes at its own edge.
/// </summary>
public interface IFontPackStore
{
    /// <summary>
    /// Upserts a whole pack (by <paramref name="slug"/>) AND replaces every one of its faces, in ONE
    /// transaction (SPEC F104 "Data model"): a re-install of an already-installed slug replaces
    /// <c>family</c>/<c>definition</c>/<c>imported_from</c>/<c>imported_at</c> on the pack row AND
    /// deletes-then-reinserts every <c>station.font_pack_face</c> row scoped to that pack — a face
    /// dropped from the reinstalled pack's own <paramref name="faces"/> list is gone from the store,
    /// never left orphaned from a stale install. <c>imported_at</c> is always the write's own
    /// <c>now()</c>, mirroring <see cref="IThemeStore.UpsertAsync"/>'s own "a re-import refreshes the
    /// stamp" rule.
    /// </summary>
    Task UpsertAsync(
        string slug, string family, string definition, string importedFrom,
        IReadOnlyList<FontPackFaceInput> faces, CancellationToken ct);

    /// <summary>
    /// Every installed pack, each with its own <see cref="FontPack.Faces"/>, in no particular
    /// guaranteed order — the library page (T203) and <c>InstalledFontCatalog</c> (T199/T200) both
    /// fold these into their own vendored ∪ installed view. Deliberately metadata-only per
    /// face (no <c>bytes</c>): a listing has no use for every face's raw payload, unlike
    /// <see cref="GetFaceByFileAsync"/>'s own hot path.
    /// </summary>
    Task<IReadOnlyList<FontPack>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// The face's raw bytes plus just enough content metadata to serve it, by its serving-key
    /// <paramref name="file"/>, or <see langword="null"/> if no such face is installed — the widened
    /// <c>/fonts/{file}</c> route's (T200) hot path once a request falls through the vendored literal
    /// switch. Deliberately bytes-and-hash only (no <c>style</c>/pack identity): every installed face
    /// serves the same <c>font/woff2</c> content type, so nothing else this table knows belongs on
    /// this path.
    /// </summary>
    Task<FontPackFaceContent?> GetFaceByFileAsync(string file, CancellationToken ct);
}
