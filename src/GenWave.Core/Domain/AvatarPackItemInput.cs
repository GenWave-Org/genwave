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
/// <param name="Sha256">The payload's hash — the store still persists whatever the caller already
/// computed rather than recomputing it here itself (this SEAM's own "the store persists, it does not
/// verify" discipline, same shape as <see cref="FontPackFaceInput.Sha256"/>'s own doc). UNLIKE that
/// sibling type, though, this is NOT the catalog's own pinned asset hash: <c>AvatarPackController</c>
/// (PLAN T293, the seam's first real write consumer) runs every fetched PNG through
/// <c>GenWave.Host.Images.ImageNormalizeService</c> BEFORE ever constructing one of these — SPEC
/// F129.2's "served bytes are metadata-free by construction" promise means what is actually stored is
/// a freshly re-encoded 512×512 derivative, never the verbatim fetched bytes, so a hash pinned from
/// the FETCH would describe bytes this row does not even hold. This is instead
/// <c>ImageNormalizeService.NormalizeAsync</c>'s own freshly-computed hash of its OWN output — the
/// fetched asset's hash is verified in-transport, once, by <c>CatalogProxyService</c> (against the
/// index's declared sha256) and is never carried any further forward; this field only ever describes
/// what is actually persisted.</param>
/// <param name="SuggestedPersona">A slug hint the apply-from-pack picker highlights (T296) —
/// <see langword="null"/> when the pack manifest names none.</param>
public sealed record AvatarPackItemInput(string Name, byte[] Bytes, string Sha256, string? SuggestedPersona = null)
{
    /// <summary>The payload's byte count — see <see cref="FontPackFaceInput.ByteSize"/>'s own remarks
    /// for why this is a derived property, never an independently-settable parameter.</summary>
    public int ByteSize => Bytes.Length;
}
