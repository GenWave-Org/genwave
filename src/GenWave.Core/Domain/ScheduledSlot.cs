namespace GenWave.Core.Domain;

/// <summary>
/// One <c>station.segment_schedule</c> row naming a block among the offenders blocking a delete —
/// shared by two consumers with the identical naming need: the persona guard (SPEC F91.9; STORY-247,
/// PLAN T121), the payload <see cref="PersonaWriteResult.ScheduledElsewhere"/> carries so
/// <c>PersonaController.Delete</c>'s 409 body can name every slot rather than staying generic (PLAN
/// T120 scaffolding); and the show guard (SPEC F115.4; STORY-305, PLAN T240), where
/// <c>ShowsController.Delete</c> queries <c>IScheduleStore.GetSlotsByShowIdAsync</c> directly for the
/// same day/time naming — <see cref="ShowWriteResult.Referenced"/> stays a bare singleton at the
/// store seam (see its own remarks), so the endpoint re-queries rather than the store pre-fetching
/// detail neither persona nor show writes always need. Deliberately a narrower read projection than
/// <see cref="ScheduleSegment"/> — a caller blocked from deleting something needs to know WHEN it is
/// still on-air, never the row's id or its genre/energy envelope — so this carries only
/// <see cref="Day"/>/<see cref="StartMinute"/>/<see cref="EndMinute"/>, the same day-of-week/minute
/// vocabulary <see cref="ScheduleSegment"/> and db/27 both already use.
/// </summary>
public sealed record ScheduledSlot(DayOfWeek Day, int StartMinute, int EndMinute);
