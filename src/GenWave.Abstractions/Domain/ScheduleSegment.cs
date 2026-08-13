namespace GenWave.Core.Domain;

/// <summary>
/// One row of the weekly format-clock grid (SPEC F91.1, db/27) — the on-air assignment for a single
/// day-of-week half-hour range. <see cref="Day"/> IS <see cref="System.DayOfWeek"/>'s own 0-6
/// numbering (0 = Sunday) — the exact numbering the database's own <c>day_of_week</c> CHECK
/// constraint enforces, so no translation ever happens between this type and the stored column.
///
/// <para>
/// <see cref="Id"/> is <see langword="null"/> for a not-yet-persisted row — e.g. one entry of a
/// week document a caller is about to hand to <c>IScheduleStore.ReplaceWeekAsync</c>,
/// before the store assigns it a fresh id. <see cref="PersonaId"/> null means music-only (F91.1);
/// <see cref="Genres"/>/<see cref="EnergyMin"/>/<see cref="EnergyMax"/> null means "use the
/// station-default envelope" (F91.4) — each is independently nullable, so a segment may override only
/// one of the three while the others fall back to the station default.
/// </para>
///
/// <para>
/// <see cref="Show"/> (SPEC F116.1, STORY-306, PLAN T241) is this block's own named show, or
/// <see langword="null"/> for an unnamed (painted-persona-only or music-only) block —
/// <c>GenWave.MediaLibrary.Station.ScheduleRepository</c> resolves it at LOAD time (a join against
/// <c>station.show</c> keyed by <c>segment_schedule.show_id</c>), never a per-tick lookup, so the week
/// snapshot this record is part of already carries every block's show identity in memory before
/// <c>ScheduleResolver</c> ever runs. Defaults to <see langword="null"/> so every pre-T241
/// construction site (test fixtures included) stays diff-free — the additive-null-member shape SPEC
/// F116.1's own "showless station byte-identical" test leans on.
/// </para>
///
/// <para>
/// <b>PLAN T243 — <see cref="ShowId"/> is the write-authoritative field; <see cref="Show"/> stays the
/// load-time projection.</b> A caller building a segment to WRITE (the PUT wire's
/// <c>ScheduleController.ToSegment</c>, or any future caller of
/// <c>IScheduleStore.ReplaceWeekAsync</c>) sets only <see cref="ShowId"/> — the
/// bare foreign key <c>segment_schedule.show_id</c> actually stores — never fabricates a
/// <see cref="ShowSummary"/> with invented <c>Name</c>/<c>Tagline</c>/<c>Flavor</c> just to carry an id
/// through. <c>ScheduleRepository</c>'s own load path sets BOTH fields from the same row (so a loaded
/// segment's <c>ShowId</c> and <c>Show?.Id</c> always agree), but every other reader —
/// <see cref="ScheduleWeekVersion.Compute"/> included — reads <see cref="ShowId"/> alone, so a writer
/// and the load-time projection can never disagree about which field means "the show this block
/// carries."
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
    double? EnergyMax,
    ShowSummary? Show = null,
    long? ShowId = null);
