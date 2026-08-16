using GenWave.Core.Abstractions;

namespace GenWave.Host.Artwork;

/// <summary>
/// The ONE identity-agreement gate (SPEC F129.6, PLAN T300 fix round F4) shared by
/// <see cref="Api.SpectatorController"/>'s <c>djAvatarUrl</c> payload field and
/// <see cref="Engine.ArtworkUrlResolver"/>'s <c>url=</c> stream annotation — before this type the
/// identical check lived as two independent copies that happened to agree; a drift between them
/// would have let the payload and the stream silently disagree on which face belongs to the on-air
/// voice.
/// <para>
/// <b>THE INVARIANT: the payload and the stream must never disagree on the face.</b> Both call
/// sites resolve THIS SAME candidate-then-confirm shape: read the accessor's synchronous,
/// zero-I/O on-air persona id as a candidate, then confirm <see cref="IActivePersonaAccessor.TryGetCachedName"/>
/// for that id agrees with the item/snapshot's own display-name attribution before trusting it. A
/// null cached name — the id was never resolved through the ordinary orchestration path, including
/// the process-boot window — counts as "can't verify" and therefore as disagreement, never a free
/// pass: no face is always safer than the WRONG face.
/// </para>
/// </summary>
public static class DjIdentity
{
    /// <summary>
    /// True only when <paramref name="accessor"/>'s cached display name for
    /// <paramref name="personaId"/> is non-null AND matches <paramref name="djName"/> exactly
    /// (<see cref="StringComparison.Ordinal"/> — this codebase's existing DJ-name comparison
    /// convention).
    /// </summary>
    public static bool Agrees(IActivePersonaAccessor accessor, long personaId, string? djName) =>
        accessor.TryGetCachedName(personaId) is { } cachedName
        && string.Equals(cachedName, djName, StringComparison.Ordinal);
}
