namespace GenWave.Core.Domain;

/// <summary>
/// One row of <c>station.announcement</c>'s history (SPEC F146.2, STORY-361, PLAN T344) — the F143.2
/// visible-decline/visible-expiry surface's own read shape, carried across the Core boundary the
/// same way <see cref="AnnouncementItem"/> carries the narrower vend-side shape one seam over.
///
/// <para>
/// <see cref="State"/> travels as the store's own lowercase text
/// (<c>"pending"|"claimed"|"aired"|"expired"|"declined"</c>) rather than a Core-level enum — mirrors
/// <c>AnnouncementRow.Source</c>'s own "raw text across the seam" restraint
/// (<c>GenWave.MediaLibrary.Station.AnnouncementRepository</c>'s remarks): <c>GET /api/announcements</c>
/// has exactly one consumer (<c>AnnouncementsController</c>) and it serializes <see cref="State"/>
/// straight onto the wire for the admin page's own state chips (F146.2), so a typed enum here would
/// exist only to be immediately stringified back by the one caller that reads it.
/// </para>
/// </summary>
/// <param name="Id">The store row's identity.</param>
/// <param name="Message">The announcement text as the owner wrote it.</param>
/// <param name="Verbatim">Whether this announcement was submitted for a word-for-word read
/// (SPEC F144.2) rather than DJ-flavored copy (F144.3).</param>
/// <param name="State">The row's current lifecycle state, lowercase (SPEC F143.2).</param>
/// <param name="DeclineReason">Non-null exactly when <see cref="State"/> is <c>"declined"</c>
/// (SPEC F143.2/F143.4/F145.2's own decline reasons).</param>
/// <param name="CollapseCount">How many case-folded-identical submissions folded into this row
/// (SPEC F143.5) — 1 when none did.</param>
/// <param name="CreatedAt">When this row was first accepted.</param>
/// <param name="ExpiresAt">When this row's TTL lapses (SPEC F143.1).</param>
/// <param name="AiredAt">Non-null exactly when a genuine <c>TrackAired</c> observation stamped this
/// row (SPEC F143.3) — null for every other state.</param>
public sealed record AnnouncementHistoryEntry(
    long Id,
    string Message,
    bool Verbatim,
    string State,
    string? DeclineReason,
    int CollapseCount,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? AiredAt);
