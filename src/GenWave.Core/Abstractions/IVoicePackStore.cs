using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F164.5/F164.6/F166.4; STORY-395/396/398/401; PLAN T413) — persistence for voice packs
/// installed from the Community Catalog's <c>voice-pack</c> kind, in
/// <c>station.voice_pack</c>(+<c>_voice</c>). Physically in the <c>GenWave.Core</c> project, NOT the
/// published <c>GenWave.Abstractions</c> NuGet, despite sharing the <c>GenWave.Core.Abstractions</c>
/// namespace with types (like <see cref="ITtsVoiceLister"/>) that DO live in that NuGet — this seam has
/// no consumer outside this solution, so it needs no wider publication and no version bump. Mirrors
/// <see cref="IFontPackStore"/>'s own "raw serialized definition string, never a Host-side manifest
/// type" discipline: <c>definition</c> stays opaque jsonb text here — this project knows nothing of
/// <c>GenWave.Host.Catalog.CatalogVoicePackManifest</c> (downstream of this seam), so the controller
/// (de)serializes at its own edge.
///
/// <para>
/// PLAN T413 shipped no listing route, so this seam originally carried no <c>GetAllAsync</c>/read-model
/// method (YAGNI). PLAN T419 widened it with <see cref="ListAsync"/> for the attributions endpoint —
/// narrower than <see cref="IFontPackStore.GetAllAsync"/>'s own richer <c>FontPack</c> projection: this
/// store still hands back only the raw <c>definition</c> text, never a voice roster, since the
/// attributions surface (SPEC F169.2) needs only the pack-level manifest to reconstitute at its own
/// edge.
/// </para>
/// </summary>
public interface IVoicePackStore
{
    /// <summary>
    /// Advisory pre-check (SPEC F166.4): names every candidate id in <paramref name="voiceIds"/> that is
    /// ALREADY installed under some OTHER pack (rows owned by <paramref name="excludingSlug"/> itself are
    /// never reported — a re-install of the same pack naming its own already-installed ids is an upsert,
    /// not a collision). Run by the controller BEFORE fetching any asset, so a doomed install fails fast
    /// without ever touching disk. This is advisory, not the enforcement point — see
    /// <see cref="UpsertAsync"/>'s own <see cref="VoicePackUpsertResult.VoiceIdCollision"/> remarks for
    /// the real, race-proof backstop.
    /// </summary>
    Task<IReadOnlyList<VoicePackVoiceOwner>> FindInstalledVoiceIdsAsync(
        IReadOnlyList<string> voiceIds, string excludingSlug, CancellationToken ct);

    /// <summary>
    /// Every voice id currently installed under this exact <paramref name="slug"/> (empty if no such
    /// pack is installed yet). Lets the controller subtract a pack's OWN already-installed ids from
    /// the STOCK roster it separately checks before the stock set is even consulted: once installed,
    /// a pack's voices live on the same flat, shared volume Kokoro itself scans (PLAN T412), so
    /// Kokoro's own <c>/v1/audio/voices</c> listing reports them as "stock" too by the time a
    /// re-install of that same pack runs its own collision check — without this method a re-install
    /// is wrongly refused as "used by stock" (SPEC F164.5's own upsert contract, PLAN T413 review
    /// round 1 finding F1).
    /// </summary>
    Task<IReadOnlyList<string>> FindVoiceIdsForPackAsync(string slug, CancellationToken ct);

    /// <summary>
    /// Upserts a whole pack (by <paramref name="slug"/>) AND replaces every one of its voices, in ONE
    /// transaction (SPEC F164.5 "Data model"): a re-install of an already-installed slug replaces
    /// <paramref name="engine"/>/<paramref name="definition"/>/<paramref name="importedFrom"/> on the
    /// pack row AND deletes-then-reinserts every <c>station.voice_pack_voice</c> row scoped to that
    /// pack — a voice dropped from the reinstalled pack's own <paramref name="voices"/> list is gone
    /// from the store, never left orphaned from a stale install. Mirrors
    /// <see cref="IFontPackStore.UpsertAsync"/>'s own shape exactly, with
    /// <see cref="VoicePackUpsertResult.VoiceIdCollision"/> standing in for
    /// <c>FontPackUpsertResult.FileCollision</c> — the caller (the controller) writes every <c>.pt</c>
    /// file to <c>Packs:VoicesRoot</c> BEFORE calling this method, so on a collision it must unlink what
    /// it just wrote itself; this seam never touches the filesystem.
    /// </summary>
    Task<VoicePackUpsertResult> UpsertAsync(
        string slug, string engine, string definition, string importedFrom,
        IReadOnlyList<VoicePackVoiceInput> voices, CancellationToken ct);

    /// <summary>
    /// Removes an installed pack by <paramref name="slug"/> (SPEC F164.6, STORY-401) — refused, naming
    /// every referencing ad spot and persona, while either still names one of its voice ids; with
    /// neither, the pack row — and, by <c>ON DELETE CASCADE</c>, every one of its
    /// <c>station.voice_pack_voice</c> rows — is removed in the SAME statement that checks for
    /// references. "The delete IS the guard", mirroring <see cref="IFontPackStore.DeleteAsync"/>'s own
    /// idiom: there is no separate ROUND TRIP between checking and deleting for a caller to get out of
    /// sync on, or a race to land inside.
    ///
    /// <para>
    /// <b>Honest boundary</b>: one atomic STATEMENT, not serializable isolation — under Postgres's
    /// default READ COMMITTED, a write that commits a newly-referencing row in the narrow window between
    /// this statement's own snapshot and its commit is not guaranteed to be seen, and the pack could
    /// still be removed. See <c>VoicePackRepository.DeleteAsync</c>'s own remarks for the exact guard SQL
    /// and this same fail-soft trade-off, applied here to two references instead of <c>IFontPackStore</c>'s
    /// one.
    /// </para>
    /// </summary>
    Task<VoicePackDeleteResult> DeleteAsync(string slug, CancellationToken ct);

    /// <summary>
    /// Every installed voice pack's <see cref="InstalledPackDefinition.Slug"/> and raw
    /// <c>definition</c> jsonb text, ordered by slug (SPEC F169.2, STORY-404, PLAN T419) — the read
    /// model the attributions endpoint reconstitutes into <c>CatalogVoicePackManifest</c> at its own
    /// edge. See this interface's own remarks on why this widened a seam PLAN T413 had deliberately
    /// left narrow.
    /// </summary>
    Task<IReadOnlyList<InstalledPackDefinition>> ListAsync(CancellationToken ct);
}
