namespace GenWave.Core.Domain;

/// <summary>
/// One item <see cref="Abstractions.IAvatarPackStore.UpsertAsync"/> writes as part of a pack install
/// (SPEC F128, STORY-332, PLAN T290) — the write-side counterpart to the read-side
/// <see cref="AvatarPackItem"/>, mirroring <see cref="FontPackFaceInput"/>'s own split for exactly the
/// same reason: <see cref="ByteSize"/> is ALWAYS derived from <see cref="Bytes"/>.<c>Length</c> rather
/// than a separately-settable constructor parameter, so a caller can never construct an instance whose
/// declared size disagrees with its own payload.
/// </summary>
/// <param name="Name">The item's name within its pack — see <see cref="AvatarPackItem.Name"/>'s own
/// remarks.</param>
/// <param name="Bytes">The item's raw payload — the exact bytes <c>station.avatar_pack_item.bytes</c>
/// stores.</param>
/// <param name="Sha256">The payload's hash, PINNED from the catalog's own already-verified asset hash
/// (mirrors <see cref="FontPackFaceInput.Sha256"/>'s own "the store persists whatever the caller
/// already trusts" discipline) rather than recomputed here.</param>
/// <param name="SuggestedPersona">A slug hint the apply-from-pack picker highlights (T296) —
/// <see langword="null"/> when the pack manifest names none.</param>
public sealed record AvatarPackItemInput(string Name, byte[] Bytes, string Sha256, string? SuggestedPersona = null)
{
    /// <summary>The payload's byte count — see <see cref="FontPackFaceInput.ByteSize"/>'s own remarks
    /// for why this is a derived property, never an independently-settable parameter.</summary>
    public int ByteSize => Bytes.Length;
}
