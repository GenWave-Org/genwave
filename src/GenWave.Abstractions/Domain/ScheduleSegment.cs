using GenWave.Abstractions.Playout;

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
    long? ShowId = null)
{
    /// <summary>
    /// This block's own effective on-air envelope (SPEC F91.4, F152.3; T376 review MED-4 — RULED
    /// IN: the per-field fallback must be ONE piece of code) — extracted here, byte-for-byte, from
    /// <c>GenWave.Orchestration.ScheduleResolver.BuildSegmentEnvelope</c> (T119's own original), so
    /// a second consumer (<c>GenWave.MediaLibrary.Garden.UnreachableGardenerPass</c>, T376) shares
    /// this ONE formula instead of re-deriving it independently. <see cref="Genres"/> falls back to
    /// <paramref name="stationDefault"/>'s own <c>Genres</c> when null; <see cref="EnergyMin"/>/
    /// <see cref="EnergyMax"/> EACH fall back to <paramref name="stationDefault"/>'s own
    /// <c>EnergyRange.Min</c>/<c>Max</c> independently (this record's own remarks above: "each is
    /// independently nullable, so a segment may override only one of the three"); <see cref="Show"/>'s
    /// own <see cref="ShowSummary.Rotation"/> rides straight through as plainly <c>Show?.Rotation</c>
    /// below — SPEC F152.3's own <c>block.Rotation ?? show.Rotation ?? null</c> formula, with no
    /// block-level rotation source existing in v1 at all (ARCHITECTURE.md's own "Rejected:
    /// block-only predicate — the card can't carry the rule"). T376 review round-3 (RULED): THIS is
    /// now the one and only chokepoint that formula runs through — <c>ScheduleResolver.ResolveRotation</c>,
    /// the prior internal "block ?? show" chokepoint, was deleted once it had zero production callers
    /// left; a future slice that DOES add a block-level rotation source widens the
    /// <c>Rotation = Show?.Rotation</c> line below, and only that line, to <c>blockRotation ?? Show?.Rotation</c>
    /// — no second place to remember to update.
    /// <see cref="SegmentEnvelope.StartsAt"/>/<see cref="SegmentEnvelope.EndsAt"/> reproduce
    /// <c>ScheduleResolver.ToTimeOnly</c>'s own 1440-minute "runs to midnight" clamp — a schema
    /// <c>EndMinute</c> of 1440 cannot be represented as a <see cref="TimeOnly"/> without wrapping to
    /// 00:00, which would read as BEFORE <c>StartsAt</c>; clamped to <see cref="TimeOnly.MaxValue"/>
    /// instead. <c>ScheduleResolver</c> now calls THIS method rather than its own hand-built block —
    /// <c>tests/GenWave.Orchestration.Tests</c>'s existing resolver facts are the proof the
    /// extraction changed no observable behaviour.
    /// </summary>
    public SegmentEnvelope EffectiveEnvelope(SegmentEnvelope stationDefault) => new(
        ToTimeOnly(StartMinute),
        ToTimeOnly(EndMinute),
        Genres ?? stationDefault.Genres,
        new EnergyRange(
            EnergyMin ?? stationDefault.EnergyRange.Min,
            EnergyMax ?? stationDefault.EnergyRange.Max))
    {
        Rotation = Show?.Rotation,
    };

    /// <summary>A schedule day never carries more minutes than this (SPEC F91.1's own CHECK,
    /// db/27) — named, not the bare literal <see cref="ToTimeOnly"/> switches on twice.</summary>
    const int MinutesPerDay = 1440;

    /// <summary>Converts a schedule minute-of-day into a display <see cref="TimeOnly"/> — mirrors
    /// <c>ScheduleResolver.ToTimeOnly</c> exactly (see that method's own remarks for the
    /// 1440/midnight clamp rationale). Duplicated rather than shared across assemblies: this
    /// Abstractions-layer type may not take a dependency on <c>GenWave.Orchestration</c> (L4/L10),
    /// so <c>ScheduleResolver</c> is the one that now calls INTO this copy instead.</summary>
    static TimeOnly ToTimeOnly(int minute) => minute switch
    {
        <= 0 => TimeOnly.MinValue,
        >= MinutesPerDay => TimeOnly.MaxValue,
        _ => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute)),
    };
}
