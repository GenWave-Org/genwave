using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F128, STORY-332, PLAN T290) — persistence for Dean-curated avatar packs installed from
/// the Community Catalog's <c>avatar</c> kind, in <c>station.avatar_pack</c>(+<c>_item</c>). Ships
/// dark: no consumer lands with this seam yet — <c>POST /api/avatar-packs/{slug}/install</c> (T293) is
/// the first write consumer, the Wardrobe Avatars tab (T294) and the apply-from-pack picker (T295/T296)
/// the first read consumers. Mirrors <see cref="IFontPackStore"/>'s own "raw serialized definition
/// string, never a Host-side manifest type" discipline: <c>definition</c> stays opaque jsonb text here.
///
/// Unlike <see cref="IFontPackStore.DeleteAsync"/>, uninstalling a pack needs NO referenced-by guard
/// (ARCHITECTURE.md's own ruling, "assignment copies, provenance records"): a worn face is a COPY, not
/// a live reference into this store, so removing a pack can never blank a DJ's face mid-broadcast.
/// </summary>
public interface IAvatarPackStore
{
    /// <summary>
    /// Upserts a whole pack (by <paramref name="slug"/>) AND replaces every one of its items, in ONE
    /// transaction (mirrors <see cref="IFontPackStore.UpsertAsync"/>'s own "replace-on-reinstall"
    /// shape): a re-install of an already-installed slug replaces <c>definition</c>/<c>imported_from</c>/
    /// <c>imported_at</c> on the pack row AND deletes-then-reinserts every
    /// <c>station.avatar_pack_item</c> row scoped to that pack. <c>imported_at</c> is always the
    /// write's own <c>now()</c>. Unlike <see cref="IFontPackStore.UpsertAsync"/>, <c>(pack_id, name)</c>
    /// is UNIQUE only WITHIN a pack (not globally, the way <c>font_pack_face.file</c> is), so this
    /// method has no cross-pack collision to translate into a rich result — a plain
    /// <see cref="Task"/> is enough.
    /// </summary>
    Task UpsertAsync(
        string slug, string definition, string importedFrom,
        IReadOnlyList<AvatarPackItemInput> items, CancellationToken ct);

    /// <summary>The installed pack identified by <paramref name="slug"/>, WITH every one of its items
    /// (bytes included — see <see cref="AvatarPack.Items"/>'s own remarks), or <see langword="null"/>
    /// if no such pack is installed.</summary>
    Task<AvatarPack?> GetBySlugAsync(string slug, CancellationToken ct);

    /// <summary>
    /// Every installed pack, in no particular guaranteed order, each with its own
    /// <see cref="AvatarPackSummary.Items"/> — name and suggested-persona metadata for every item, but
    /// NEVER their bytes (review finding B1: the shelf/wardrobe listing this feeds has no use for a raw
    /// payload it would only discard, and <see cref="AvatarPackItemSummary"/> is structurally incapable
    /// of carrying one). Mirrors <see cref="IFontPackStore.GetAllAsync"/>'s own "listing is
    /// metadata-only, in ONE query" contract — a caller needing an item's actual bytes reads
    /// <see cref="GetBySlugAsync"/> instead, whose <see cref="AvatarPack.Items"/> stays the
    /// bytes-carrying shape.
    /// </summary>
    Task<IReadOnlyList<AvatarPackSummary>> GetAllAsync(CancellationToken ct);

    /// <summary>Removes the pack identified by <paramref name="slug"/> — and, by
    /// <c>station.avatar_pack_item</c>'s own <c>ON DELETE CASCADE</c>, every one of its items — in the
    /// same statement. Returns <see langword="true"/> when a pack was deleted, <see langword="false"/>
    /// when no such pack existed. No referenced-by guard (see this interface's own remarks).</summary>
    Task<bool> DeleteAsync(string slug, CancellationToken ct);
}
