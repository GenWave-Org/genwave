namespace GenWave.Core.Domain;

/// <summary>
/// One <c>station.segment_schedule</c> row naming a persona among the offending slots blocking a
/// delete (SPEC F91.9; STORY-247, PLAN T121) — the payload
/// <see cref="PersonaWriteResult.ScheduledElsewhere"/> carries so <c>PersonaController.Delete</c>'s
/// 409 body can name every slot rather than staying generic (PLAN T120 scaffolding). Deliberately a
/// narrower read projection than <see cref="ScheduleSegment"/> — a caller blocked from deleting a
/// persona needs to know WHEN it is still on-air, never the row's id or its genre/energy envelope —
/// so this carries only <see cref="Day"/>/<see cref="StartMinute"/>/<see cref="EndMinute"/>, the
/// same day-of-week/minute vocabulary <see cref="ScheduleSegment"/> and db/27 both already use.
/// </summary>
public sealed record ScheduledSlot(DayOfWeek Day, int StartMinute, int EndMinute);
