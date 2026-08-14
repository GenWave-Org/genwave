namespace GenWave.Core.Domain;

/// <summary>
/// One <c>station.schedule_special</c> row naming a blocking dated special among the offenders
/// blocking a delete (gh-#462) — the <see cref="ScheduledSlot"/> sibling for a DATE-scoped need:
/// <see cref="ScheduledSlot"/> carries a repeating <see cref="DayOfWeek"/>, but a special names ONE
/// calendar date, so <see cref="OnDate"/> replaces <see cref="ScheduledSlot.Day"/> —
/// <see cref="StartMinute"/>/<see cref="EndMinute"/> keep <see cref="ScheduledSlot"/>'s own identical
/// minute vocabulary.
///
/// <para>
/// Shares <see cref="ScheduledSlot"/>'s own narrow-read-projection philosophy verbatim (see that
/// type's own remarks) — deliberately NOT the full <see cref="ScheduleSpecial"/> row: a caller
/// blocked from deleting a persona needs to know WHEN it is still on-air via a dated special, never
/// the row's id or its persona/show/genre/energy detail. Carried by
/// <see cref="PersonaWriteResult.ScheduledElsewhere"/>'s <see cref="PersonaWriteResult.ScheduledElsewhere.Specials"/>
/// alongside <see cref="ScheduledSlot"/>'s own <see cref="PersonaWriteResult.ScheduledElsewhere.Slots"/>,
/// so <c>PersonaController.Delete</c>'s 409 body can NAME a blocking dated special the way
/// <c>ShowsController.Delete</c> already does — that guard already has the full
/// <see cref="ScheduleSpecial"/> row on hand via <c>IScheduleSpecialStore.ListUpcomingAsync</c>; this
/// one instead pre-queries <c>station.schedule_special</c> directly (<c>PersonaRepository.DeleteAsync</c>)
/// and has no reason to carry more than a blocked caller needs.
/// </para>
/// </summary>
public sealed record ScheduledSpecialSlot(DateOnly OnDate, int StartMinute, int EndMinute);
