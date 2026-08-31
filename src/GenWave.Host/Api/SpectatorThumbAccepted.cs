namespace GenWave.Host.Api;

/// <summary>
/// The fixed 202 body for <c>POST /spectator/api/thumbs</c> (SPEC F150.3, STORY-369, PLAN T366) —
/// byte-identical whether the token named a current airing, a previous one, or resolved to nothing at
/// all (SPEC F150.3's no-oracle posture, the exact <see cref="SpectatorRequestAccepted"/> precedent
/// one seam over): no row id, no <see cref="GenWave.Core.Domain.ThumbWriteResult"/> disclosure, no
/// hint about whether anything was actually written. This type deliberately has NO constructor
/// parameters — there is nothing here for a caller to influence, so nothing here can vary by accident.
/// </summary>
public sealed record SpectatorThumbAccepted
{
    /// <summary>Always <c>"received"</c> — the one fixed status literal (SPEC F150.3).</summary>
    public string Status => "received";

    /// <summary>
    /// Always the same disclaimer: no confirmation of what, if anything, changed on this track's
    /// rotation signal is ever exposed (SPEC F150.3 — no oracle, ever, for any token or direction).
    /// Plain ASCII, deliberately (mirrors <see cref="SpectatorRequestAccepted.Note"/>'s own text):
    /// keeps this constant byte-identical across every JSON encoder's default non-ASCII escaping
    /// policy, never a distraction from what this field pins.
    /// </summary>
    public string Note => "Thanks for the thumb - it shapes rotation over time, with no per-track confirmation.";
}
