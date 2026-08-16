using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F128-F129, STORY-333, PLAN T290) — persistence for the worn face in
/// <c>station.persona_avatar</c> (a 1:1 <c>station.persona</c> extension, the F33 <c>media_rating</c>
/// precedent). Ships dark: no consumer lands with this seam yet — <c>PersonaAvatarController</c> (T295,
/// the upload/remove/apply-from-pack write paths) and the Personas UI (T296) are the first Host call
/// sites; <c>SpectatorArtworkController</c> (T298) and <c>ArtworkUrlResolver</c> (T300) are the first
/// read consumers of <see cref="GetByTokenAsync"/>.
///
/// Deliberately a DUMB store — token generation and rotation policy belong to the caller (T295), not
/// this seam: <see cref="UpsertAsync"/> takes a <see cref="PersonaAvatarInput"/> carrying an
/// already-chosen <see cref="PersonaAvatarInput.Token"/>, and simply persists it (mirrors
/// <see cref="IFontPackStore"/>'s own "the store does not compute, it stores what the caller already
/// decided" discipline for <c>byte_size</c>/hash). <see cref="PersonaAvatarInput"/> is a distinct type
/// from the read-side <see cref="PersonaAvatar"/> deliberately — see its own remarks.
/// </summary>
public interface IPersonaAvatarStore
{
    /// <summary>The worn face for <paramref name="personaId"/>, or <see langword="null"/> if that
    /// persona has none — <c>station.persona_avatar.persona_id</c> is <c>UNIQUE</c>, so at most one row
    /// can ever match.</summary>
    Task<PersonaAvatar?> GetByPersonaIdAsync(long personaId, CancellationToken ct);

    /// <summary>The worn face serving under <paramref name="token"/>, or <see langword="null"/> if no
    /// row carries it — the read path <c>SpectatorArtworkController</c>'s DJ-token route (T298)
    /// resolves against; an unknown/stale token (the prior rotation) is an ordinary miss here, never an
    /// error, mirroring the F88 opaque-token "no oracle" posture.</summary>
    Task<PersonaAvatar?> GetByTokenAsync(string token, CancellationToken ct);

    /// <summary>
    /// Upserts by <paramref name="avatar"/>.<see cref="PersonaAvatarInput.PersonaId"/> (SPEC F129.1): a
    /// persona with no existing face inserts a row, one with an existing face replaces every column —
    /// including <see cref="PersonaAvatarInput.Token"/>, which the CALLER has already rotated before
    /// this method ever runs (T295's own responsibility, not this seam's). <c>updated_at</c> is always
    /// the write's own <c>now()</c>.
    /// </summary>
    Task UpsertAsync(PersonaAvatarInput avatar, CancellationToken ct);

    /// <summary>Removes the worn face for <paramref name="personaId"/>, if any. Returns
    /// <see langword="true"/> when a row was deleted, <see langword="false"/> when that persona already
    /// had none — never throws for a missing row (mirrors <see cref="IScheduleSpecialStore.DeleteAsync"/>'s
    /// own "report, don't guard" shape; there is no reference to guard against, unlike
    /// <see cref="IFontPackStore.DeleteAsync"/>).</summary>
    Task<bool> DeleteAsync(long personaId, CancellationToken ct);
}
