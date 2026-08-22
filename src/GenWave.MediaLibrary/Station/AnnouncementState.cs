namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The <c>station.announcement</c> state machine (SPEC F143.2), MediaLibrary-internal — no
/// <c>GenWave.Core</c> seam exists yet for announcements (see <see cref="AnnouncementRepository"/>'s
/// own remarks on why). Total and five-valued, mirroring the DDL's own CHECK constraint exactly:
/// <c>Pending -&gt; Claimed -&gt; Aired</c>; <c>Claimed -&gt; Pending</c> (re-arm);
/// <c>Pending|Claimed -&gt; Expired</c>; <c>Pending|Claimed -&gt; Declined</c>. No row is ever deleted —
/// every state above is a value this enum can be read back as, never a "gone" case to model separately.
///
/// <see cref="AnnouncementStateTypeHandler"/> is the one place that maps a member here to/from the
/// column's own lowercase text value.
/// </summary>
enum AnnouncementState
{
    Pending,
    Claimed,
    Aired,
    Expired,
    Declined,
}
