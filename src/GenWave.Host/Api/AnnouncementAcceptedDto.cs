namespace GenWave.Host.Api;

/// <summary>
/// The 200 body for an accepted <c>POST /api/announcements</c> (SPEC F143.1). Deliberately minimal —
/// just the row's id, whether this call created it fresh or folded it into an already-pending
/// case-folded duplicate (SPEC F143.5): the caller never learns which happened, matching
/// <c>AnnouncementsController</c>'s own "the endpoint delegates, it never re-decides" collapse
/// posture. A later task (e.g. PLAN T344's admin page) widens this the moment a real caller needs
/// more than the id back.
/// </summary>
public sealed record AnnouncementAcceptedDto(long Id);
