namespace GenWave.Orchestration;

/// <summary>
/// Marks a dropped slot as a <see cref="SegmentKind.Announcement"/> (SPEC F144.5) — a payload-less
/// marker (PLAN T536 review finding F6): the claimed row's own id already has exactly one carrier,
/// <c>PlannedSlot.Reservation</c> (SPEC F187.3 — every Announcement slot carries one), so
/// <see cref="BreakDelivery.ReportDrop"/> reads it from there (the same
/// <c>AnnouncementIdOf</c> helper <see cref="BreakDelivery.DeliverRendered"/>'s own MediaId wrap
/// already uses) instead of this policy carrying a second, independently-constructed copy that could
/// drift from it.
/// </summary>
public sealed record AnnouncementWarnSubject : WarnDropSubject;
