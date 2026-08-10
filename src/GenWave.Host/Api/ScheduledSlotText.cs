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

    // Minutes-since-midnight as HH:mm — plain arithmetic, not TimeSpan's "hh" format specifier: a
    // 1440-minute end (midnight, the grid's own maximum) rolls into TimeSpan's Days component, which
    // "hh" ignores entirely, silently printing "00:00" for what is actually the end of the day.
    internal static string FormatMinutes(int minutesSinceMidnight) =>
        $"{(minutesSinceMidnight / 60).ToString("D2", CultureInfo.InvariantCulture)}:" +
        $"{(minutesSinceMidnight % 60).ToString("D2", CultureInfo.InvariantCulture)}";
}
