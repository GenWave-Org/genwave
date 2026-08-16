namespace GenWave.Core.Domain;

/// <summary>
/// The station image <see cref="Abstractions.IStationImageStore.UpsertAsync"/> writes (SPEC F131,
/// STORY-339, PLAN T290/T307 rider) — the write-side counterpart to the read-side
/// <see cref="StationImage"/>, mirroring <see cref="PersonaAvatarInput"/>'s own split from its
/// read-side <see cref="PersonaAvatar"/> for the identical reason: this store's own two adjacent,
/// same-typed <see langword="string"/> parameters (<c>sha256</c>/<c>token</c>) had no type-level
/// guard pinning <see cref="ByteSize"/> to <see cref="Bytes"/>'s own <c>Length</c> before this type
/// existed — <see cref="ByteSize"/> is ALWAYS derived, never a separately-settable constructor
/// parameter, so a caller can never hand the store a declared size that disagrees with its own
/// payload (the exact <see cref="PersonaAvatarInput"/>/<c>AvatarPackItemInput</c> discipline, applied
/// here at the T307 second-copy moment).
/// </summary>
/// <param name="Bytes">The payload to store — the exact bytes <c>station.station_image.bytes</c> will
/// hold.</param>
/// <param name="Sha256">The payload's hash, PINNED from whatever the caller already computed (the
/// T291 pipeline's own <c>ImageNormalizeResult.Success.Sha256</c>), never recomputed here.</param>
/// <param name="Token">The token this image will serve under — already chosen/rotated by the CALLER
/// before this type is ever constructed (see <see cref="Abstractions.IStationImageStore.UpsertAsync"/>'s
/// own remarks on why rotation policy lives outside this seam).</param>
public sealed record StationImageInput(byte[] Bytes, string Sha256, string Token)
{
    /// <summary>The payload's byte count — see <see cref="PersonaAvatarInput.ByteSize"/>'s own remarks
    /// for why this is a derived property, never an independently-settable parameter.</summary>
    public int ByteSize => Bytes.Length;
}
