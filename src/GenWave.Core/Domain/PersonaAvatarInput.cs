namespace GenWave.Core.Domain;

/// <summary>
/// The face <see cref="Abstractions.IPersonaAvatarStore.UpsertAsync"/> writes (SPEC F128-F129,
/// STORY-333, PLAN T290) — the write-side counterpart to the read-side <see cref="PersonaAvatar"/>,
/// mirroring <see cref="AvatarPackItemInput"/>'s own split from its read-side <see cref="AvatarPackItem"/>
/// for exactly the same reason: <see cref="ByteSize"/> is ALWAYS derived from <see cref="Bytes"/>.
/// <c>Length</c> rather than a separately-settable constructor parameter, so a caller can never hand the
/// store a declared size that disagrees with its own payload. This type also has NO
/// <see cref="PersonaAvatar.UpdatedAt"/> member at all — the write is always stamped with the store's
/// own <c>now()</c> (<see cref="Abstractions.IPersonaAvatarStore.UpsertAsync"/>'s own remarks), so there
/// is no caller-supplied value to even consider trusting or discarding.
/// </summary>
/// <param name="PersonaId">The owning persona's id — see <see cref="PersonaAvatar.PersonaId"/>'s own
/// remarks.</param>
/// <param name="Bytes">The payload to store — the exact bytes <c>station.persona_avatar.bytes</c> will
/// hold.</param>
/// <param name="Sha256">The payload's hash, PINNED from whatever the caller already computed/verified
/// (mirrors <see cref="AvatarPackItemInput.Sha256"/>'s own "the store persists whatever the caller
/// already trusts" discipline) rather than recomputed here.</param>
/// <param name="Token">The token this face will serve under — already chosen/rotated by the CALLER
/// before this type is ever constructed (see <see cref="Abstractions.IPersonaAvatarStore.UpsertAsync"/>'s
/// own remarks on why rotation policy lives outside this seam).</param>
/// <param name="Source">See <see cref="PersonaAvatar.Source"/>'s own remarks.</param>
/// <param name="ImportedFrom">See <see cref="PersonaAvatar.ImportedFrom"/>'s own remarks.</param>
public sealed record PersonaAvatarInput(
    long PersonaId,
    byte[] Bytes,
    string Sha256,
    string Token,
    PersonaAvatarSource Source,
    string? ImportedFrom)
{
    /// <summary>The payload's byte count — see <see cref="AvatarPackItemInput.ByteSize"/>'s own remarks
    /// for why this is a derived property, never an independently-settable parameter.</summary>
    public int ByteSize => Bytes.Length;
}
