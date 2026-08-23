namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Who submitted an announcement (SPEC F143.1's "token OR admin session" door), MediaLibrary-internal —
/// mirrors <see cref="AnnouncementState"/>'s own reasoning, one column over. Only
/// <see cref="AnnouncementRepository.InsertAsync"/> consumes a value of this type today — a caller
/// picks one at submission time, mapped to the column's lowercase text value there directly. Nothing
/// yet reads <c>station.announcement.source</c> back into this type, so <see cref="AnnouncementRow.Source"/>
/// stays the column's own raw text — the same "convert explicitly only where a real caller needs the
/// typed value" restraint <see cref="PersonaAvatarRow.Source"/>'s own remarks document.
/// </summary>
enum AnnouncementSource
{
    Token,
    Session,
}
