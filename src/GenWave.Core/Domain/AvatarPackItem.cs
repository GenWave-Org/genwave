namespace GenWave.Core.Domain;

/// <summary>
/// One <c>station.avatar_pack_item</c> row, WITH its raw <see cref="Bytes"/> (SPEC F128, STORY-332,
/// PLAN T290) — unlike <see cref="Domain.FontPackFace"/>'s own metadata-only listing shape, an avatar
/// pack item's whole point is a later apply-from-pack write (T295) copying these exact bytes into a
/// <see cref="PersonaAvatar"/> row (the "assignment copies, provenance records" ruling) — a pack detail
/// page's own preview imagery instead comes from a transient PROXIED read of the catalog (the F104
/// "specimen" precedent), never from this store, so no second bytes-free projection of this type is
/// needed the way <see cref="Domain.FontPackFace"/> vs <see cref="Domain.FontPackFaceContent"/> splits
/// in two.
/// </summary>
/// <param name="Name">The item's name within its pack — unique per pack (the table's
/// <c>UNIQUE(pack_id, name)</c> constraint), scoped per-pack unlike
/// <see cref="Domain.FontPackFace.File"/>'s own globally-unique serving key.</param>
/// <param name="SuggestedPersona">A slug hint the apply-from-pack picker highlights (T296) — an OFFER,
/// never an auto-write; <see langword="null"/> when the pack manifest names none.</param>
/// <param name="Bytes">The stored 512x512 normalized PNG.</param>
/// <param name="ByteSize">The stored payload's byte count.</param>
/// <param name="Sha256">The stored payload's hash, pinned at install time.</param>
public sealed record AvatarPackItem(
    string Name,
    string? SuggestedPersona,
    byte[] Bytes,
    int ByteSize,
    string Sha256);
