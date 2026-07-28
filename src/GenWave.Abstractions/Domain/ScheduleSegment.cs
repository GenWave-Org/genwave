namespace GenWave.Core.Domain;

/// <summary>
/// One row of the weekly format-clock grid (SPEC F91.1, db/27) — the on-air assignment for a single
/// day-of-week half-hour range. <see cref="Day"/> IS <see cref="System.DayOfWeek"/>'s own 0-6
/// numbering (0 = Sunday) — the exact numbering the database's own <c>day_of_week</c> CHECK
/// constraint enforces, so no translation ever happens between this type and the stored column.
///
/// <para>
/// <see cref="Id"/> is <see langword="null"/> for a not-yet-persisted row — e.g. one entry of a
/// week document a caller is about to hand to <see cref="Abstractions.IScheduleStore.ReplaceWeekAsync"/>,
/// before the store assigns it a fresh id. <see cref="PersonaId"/> null means music-only (F91.1);
/// <see cref="Genres"/>/<see cref="EnergyMin"/>/<see cref="EnergyMax"/> null means "use the
/// station-default envelope" (F91.4) — each is independently nullable, so a segment may override only
/// one of the three while the others fall back to the station default.
/// </para>
/// </summary>
public sealed record ScheduleSegment(
    long? Id,
    DayOfWeek Day,
    int StartMinute,
    int EndMinute,
    long? PersonaId,
    IReadOnlyList<string>? Genres,
    double? EnergyMin,
    double? EnergyMax);
