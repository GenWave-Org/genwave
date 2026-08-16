namespace GenWave.Core.Domain;

/// <summary>
/// One row read back from <c>station.persona_avatar</c> (SPEC F128-F129, STORY-333, PLAN T290) — the
/// worn face, a 1:1 extension of <c>station.persona</c> (the F33 <c>media_rating</c> precedent: bytes
/// live off the hot persona row, so a card/prompt read never drags image bytes along).
/// </summary>
/// <param name="PersonaId">The owning persona's id — <c>UNIQUE</c> at the store (one face per
/// persona), <c>ON DELETE CASCADE</c> (deleting a persona deletes its own worn face with it).
/// <c>station.persona_avatar.persona_id</c> is itself <c>int4</c> (db/37), but the C# seam is
/// <see langword="long"/> — the house int4-column-behind-<see langword="long"/>-C#-seam convention
/// <see cref="Abstractions.IPersonaStore"/>/<see cref="Abstractions.IPersonaMemory"/>/
/// <see cref="Abstractions.IPersonaTasteStore"/> already carry.</param>
/// <param name="Bytes">The stored 512x512 normalized PNG, metadata-free (T291's own ffmpeg re-encode
/// strips EXIF/GPS as a side effect of the crop-and-convert).</param>
/// <param name="ByteSize">The stored payload's byte count.</param>
/// <param name="Sha256">The stored payload's hash.</param>
/// <param name="Token">The 128-bit hex opaque token this face serves under (the F88 art-transport
/// idiom) — ROTATED on every write (F129.1), so replacing a face revokes the old URL and an
/// <c>immutable</c> year-cache is always safe.</param>
/// <param name="Source">Whether this face arrived via a direct owner upload or was copied from an
/// installed <see cref="AvatarPack"/> item/persona-entry sidecar.</param>
/// <param name="ImportedFrom">The pack slug or persona-entry slug this face was copied from, when
/// <see cref="Source"/> is <see cref="PersonaAvatarSource.Catalog"/> — informational only (the
/// copy-with-provenance ruling: this column is never read back to re-fetch or re-validate the
/// face). <see langword="null"/> for an <see cref="PersonaAvatarSource.Upload"/> row.</param>
/// <param name="UpdatedAt">When this face was last written (uploaded, replaced, or applied from a
/// pack).</param>
public sealed record PersonaAvatar(
    long PersonaId,
    byte[] Bytes,
    int ByteSize,
    string Sha256,
    string Token,
    PersonaAvatarSource Source,
    string? ImportedFrom,
    DateTime UpdatedAt);
