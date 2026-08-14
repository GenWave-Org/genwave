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
    ///
    /// <para>
    /// Returns <see cref="FontPackUpsertResult.FileCollision"/> (gh-#406 slice 2) rather than letting a
    /// storage-layer unique-violation escape — <c>station.font_pack_face.file</c> is UNIQUE across
    /// every installed pack, not scoped per-pack, so a cross-pack filename clash is a real, if rare,
    /// possibility every caller must handle, never a surprise exception from a seam whose whole point
    /// is hiding the storage technology behind it.
    /// </para>
    /// </summary>
    Task<FontPackUpsertResult> UpsertAsync(
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
    /// <paramref name="file"/>, or <see langword="null"/> if no such face is installed — read by
    /// <c>InstalledFontCatalog</c>'s reload loop (T200), which snapshots faces into memory; the
    /// widened <c>/fonts/{file}</c> route serves from that snapshot and NEVER calls this per-request. Deliberately bytes-and-hash only (no <c>style</c>/pack identity): every installed face
    /// serves the same <c>font/woff2</c> content type, so nothing else this table knows belongs on
    /// this path.
    /// </summary>
    Task<FontPackFaceContent?> GetFaceByFileAsync(string file, CancellationToken ct);

    /// <summary>
    /// Removes an installed pack by <paramref name="slug"/> (SPEC F104.14, STORY-288, PLAN T208) —
    /// refused, naming every referencing owner theme, while ANY <c>station.theme</c> row still
    /// references one of its faces; with none, the pack row — and, by <c>ON DELETE CASCADE</c>, every
    /// one of its <c>station.font_pack_face</c> rows — is removed in the SAME statement that checks for
    /// references. "The delete IS the guard", mirroring <see cref="UpsertAsync"/>'s own "the insert
    /// (upsert) IS the uniqueness check" idiom: there is no separate ROUND TRIP between checking and
    /// deleting for a caller to get out of sync on (T208's own reviewer-culture obligation — the guard
    /// must not be an advisory pre-check a completely separate delete call could ignore or race past).
    ///
    /// <para>
    /// <b>Honest boundary (review finding N2) — this is one atomic STATEMENT, not serializable
    /// isolation.</b> Under Postgres's default READ COMMITTED, a single statement's own sub-query still
    /// reads a snapshot fixed at that statement's start: a save-as-own/import that COMMITS a
    /// newly-referencing <c>station.theme</c> row in the narrow window between this delete statement's
    /// own snapshot and its commit is not guaranteed to be seen, and the pack could still be removed. The
    /// outcome is fail-soft, not silently broken: the widened <c>GET /fonts/{file}</c> route (SPEC
    /// F104.6) stops serving the now-missing face on its very next request with an ordinary unknown-file
    /// 404, not a crash, and the operator's freshly-saved theme still resolves and renders
    /// — one of its two declared font roles simply falls back to whatever <c>@font-face</c> a browser
    /// substitutes for a 404'd woff2, exactly as an operator hand-editing <c>station.theme</c> to name a
    /// never-installed pack already could today. What this method's own single-statement shape DOES
    /// close is the coarser, likelier hazard: an application-level "SELECT to check, THEN a separate
    /// DELETE call" — which would leave an open window measured in ROUND TRIPS, not one statement's own
    /// execution time, for a concurrent write to land in.
    /// </para>
    ///
    /// See <c>FontPackRepository.DeleteAsync</c>'s own remarks for exactly how a reference is detected
    /// WITHOUT this seam (or its caller) ever depending on <c>GenWave.Host.Theming.ThemeManifest</c> —
    /// this project's own "opaque jsonb, deserialize at your own edge" discipline (this interface's own
    /// remarks) extended to a QUERY, not just a write — and for the false-positive direction that same
    /// substring search accepts as a deliberate, fail-closed trade-off.
    /// </summary>
    Task<FontPackDeleteResult> DeleteAsync(string slug, CancellationToken ct);
}
