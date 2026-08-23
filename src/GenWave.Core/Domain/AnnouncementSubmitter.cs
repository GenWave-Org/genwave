namespace GenWave.Core.Domain;

/// <summary>
/// Who is authenticated at the door of <c>POST /api/announcements</c> (SPEC F143.1's "token OR admin
/// session", STORY-357/360, PLAN T339/T340) — the Core-level counterpart the
/// <see cref="Abstractions.IAnnouncementStore"/> seam carries across the Host/MediaLibrary boundary.
/// Mirrors <c>GenWave.MediaLibrary.Station.AnnouncementSource</c>'s own two values exactly (that type
/// is MediaLibrary-internal, so Host cannot reference it directly — see that enum's own remarks); the
/// repository maps this one to that one at the write.
///
/// <b>Derivation rule (binding, carried from PLAN T337's review):</b> a caller always picks this from
/// the AUTHENTICATED PRINCIPAL, never from the request body — the column's own default
/// (<c>'token'</c>) looks privileged, so trusting a client-supplied value here would let any caller
/// claim the token door without ever presenting one. <see cref="Session"/> is <c>AnnouncementsController</c>'s
/// only value today (PLAN T339 ships session auth alone); <see cref="Token"/> is unreachable until the
/// Bearer scheme lands (PLAN T340).
/// </summary>
public enum AnnouncementSubmitter
{
    Session,
    Token,
}
