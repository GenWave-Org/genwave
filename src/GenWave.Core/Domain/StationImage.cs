namespace GenWave.Core.Domain;

/// <summary>
/// The single row read back from <c>station.station_image</c> (SPEC F131, STORY-339, PLAN T290, gh-#15)
/// — the owner-customized station image, the F88 artwork fallback's "row-else-shipped-logo" source and
/// the spectator/favicon surface once T307 wires it. The table itself is a deliberate single-row
/// deviation from every other table in this schema (<c>id int primary key default 1 check (id = 1)</c>)
/// — the row IS the image, so this type carries no surrogate key at all, mirroring
/// <see cref="OwnerTheme"/>/<see cref="FontPack"/>'s own "slug is identity, no exposed id" convention
/// taken one step further (there is not even a slug here — one station, one image).
/// </summary>
/// <param name="Bytes">The stored 512x512 normalized PNG, metadata-free.</param>
/// <param name="ByteSize">The stored payload's byte count.</param>
/// <param name="Sha256">The stored payload's hash.</param>
/// <param name="Token">The opaque token this image serves under (the F88 art-transport idiom) —
/// ROTATED on every write, busting any <c>immutable</c> cache. Unlike
/// <see cref="PersonaAvatar.Token"/>, not <c>UNIQUE</c> at the store — there is only ever one row, so a
/// uniqueness constraint would be a no-op.</param>
/// <param name="UpdatedAt">When this image was last written.</param>
public sealed record StationImage(
    byte[] Bytes,
    int ByteSize,
    string Sha256,
    string Token,
    DateTime UpdatedAt);
