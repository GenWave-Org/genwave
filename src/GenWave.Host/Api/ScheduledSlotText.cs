using System.Globalization;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Formats a <see cref="ScheduledSlot"/> for an operator-facing 409 detail message — shared by
/// <see cref="PersonaController.Delete"/> (SPEC F91.9, PLAN T121) and <see cref="ShowsController.Delete"/>
/// (SPEC F115.4, PLAN T240), the two delete guards that both name every blocking day/time slot the
/// identical <c>"Mon 09:00–12:00"</c> way. Extracted here (PLAN T240 review) rather than left as two
/// hand-copies: the copy on <see cref="ShowsController"/> had silently dropped the load-bearing
/// comment below on the minutes-to-HH:mm conversion — a single shared implementation makes that
/// impossible to drop a second time, on either side.
/// </summary>
internal static class ScheduledSlotText
{
    /// <summary>
    /// <c>"Mon 09:00–12:00"</c> — invariant-culture abbreviated day name (never a station-configurable
    /// locale; this is an operator-facing admin message, not station-facing broadcast copy) plus the
    /// HH:mm span from <see cref="FormatMinutes"/>.
    /// </summary>
    internal static string FormatSlot(ScheduledSlot slot) =>
        $"{CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedDayName(slot.Day)} " +
        $"{FormatMinutes(slot.StartMinute)}–{FormatMinutes(slot.EndMinute)}";

    /// <summary>
    /// <c>"2026-08-20 09:00–12:00"</c> — the <see cref="ScheduleSpecial"/> sibling of
    /// <see cref="FormatSlot"/> (SPEC F120.1, PLAN T259): <see cref="ShowsController.Delete"/>'s own
    /// guard now names a referencing dated special alongside a referencing weekly block, since
    /// <c>station.schedule_special.show_id</c> carries the identical <c>ON DELETE RESTRICT</c> FK
    /// (db/36) that already backs <see cref="FormatSlot"/>'s own callers. ISO <c>yyyy-MM-dd</c>
    /// (invariant, unambiguous — never a station-configurable locale, the same posture
    /// <see cref="FormatSlot"/>'s own abbreviated day name already takes) rather than a day name: a
    /// special names ONE calendar date, not a repeating weekday, so there is no day-of-week to
    /// abbreviate in the first place.
    /// </summary>
    internal static string FormatSpecial(ScheduleSpecial special) =>
        $"{special.OnDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} " +
        $"{FormatMinutes(special.StartMinute)}–{FormatMinutes(special.EndMinute)}";

    /// <summary>
    /// <c>"2026-08-20 09:00–12:00"</c> — the <see cref="ScheduledSpecialSlot"/> overload (gh-#462):
    /// <c>PersonaController.Delete</c>'s own guard pre-queries <c>station.schedule_special</c>
    /// directly (<c>PersonaRepository.DeleteAsync</c>) rather than through
    /// <c>IScheduleSpecialStore</c>, so it only ever has the narrow <see cref="ScheduledSpecialSlot"/>
    /// projection on hand, never a full <see cref="ScheduleSpecial"/> row — this overload formats
    /// that shape identically to <see cref="FormatSpecial(ScheduleSpecial)"/> (same ISO
    /// <c>yyyy-MM-dd</c>, same invariant-culture posture, same rationale) rather than forcing a caller
    /// to fabricate a full row's worth of nulls just to reuse the other overload.
    /// </summary>
    internal static string FormatSpecial(ScheduledSpecialSlot special) =>
        $"{special.OnDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} " +
        $"{FormatMinutes(special.StartMinute)}–{FormatMinutes(special.EndMinute)}";

    // Minutes-since-midnight as HH:mm — plain arithmetic, not TimeSpan's "hh" format specifier: a
    // 1440-minute end (midnight, the grid's own maximum) rolls into TimeSpan's Days component, which
    // "hh" ignores entirely, silently printing "00:00" for what is actually the end of the day.
    internal static string FormatMinutes(int minutesSinceMidnight) =>
        $"{(minutesSinceMidnight / 60).ToString("D2", CultureInfo.InvariantCulture)}:" +
        $"{(minutesSinceMidnight % 60).ToString("D2", CultureInfo.InvariantCulture)}";
}
