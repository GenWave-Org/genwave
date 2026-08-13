namespace GenWave.Core.Domain;

/// <summary>
/// One row of the dated-specials tail (SPEC F120.1, db/36, STORY-317, PLAN T258) — a persona/show/
/// envelope assignment for a single CALENDAR DATE's half-hour range, rather than a day-of-week's.
/// <c>ScheduleResolver</c>'s specials-first rung (SPEC F120.2) shadows
/// <see cref="ScheduleSegment"/>'s weekly grid for exactly this span on <see cref="OnDate"/> — every
/// other day this same wall-clock span still resolves through the weekly grid untouched.
///
/// <para>
/// Deliberately mirrors <see cref="ScheduleSegment"/>'s own member-for-member shape (SPEC F120.1's own
/// "F91 constraints mirrored" instruction) with exactly one substitution: <see cref="OnDate"/> (a
/// specific <see cref="DateOnly"/>) replaces <see cref="ScheduleSegment.Day"/> (a repeating
/// <see cref="DayOfWeek"/>) — a special names ONE occurrence, never a weekday that recurs.
/// <see cref="StartMinute"/>/<see cref="EndMinute"/> keep the identical 30-minute-step/range/end-after-
/// start CHECKs <c>station.segment_schedule</c> already enforces (db/06), so the resolver's rung can
/// treat a resolved special exactly like a resolved schedule block with no unit conversion anywhere.
/// <see cref="PersonaId"/> null means music-only (same F91.1 rule); <see cref="Genres"/>/
/// <see cref="EnergyMin"/>/<see cref="EnergyMax"/> null means "use the station-default envelope" (same
/// F91.4 rule) — each independently nullable, same as <see cref="ScheduleSegment"/>'s own.
/// </para>
///
/// <para>
/// <see cref="Id"/> is <see langword="null"/> for a not-yet-persisted row — e.g. a draft a caller is
/// about to hand to <c>Abstractions.IScheduleSpecialStore.CreateAsync</c>, before the store assigns it
/// a fresh id. <see cref="Show"/>/<see cref="ShowId"/> follow <see cref="ScheduleSegment"/>'s own
/// write-vs-load split exactly: <see cref="ShowId"/> is the write-authoritative bare foreign key
/// (<c>schedule_special.show_id</c>); <see cref="Show"/> is the load-time projection a repository joins
/// against <c>station.show</c> to populate, never fabricated by a writer.
/// </para>
/// </summary>
public sealed record ScheduleSpecial(
    long? Id,
    DateOnly OnDate,
    int StartMinute,
    int EndMinute,
    long? PersonaId,
    IReadOnlyList<string>? Genres,
    double? EnergyMin,
    double? EnergyMax,
    ShowSummary? Show = null,
    long? ShowId = null);
