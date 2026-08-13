namespace GenWave.Core.Domain;

/// <summary>
/// An immutable read of the entire weekly grid (SPEC F91.1, F91.3; STORY-240, PLAN T118).
/// <see cref="Segments"/> is every <c>station.segment_schedule</c> row, ordered by day then start
/// minute. A caller that wants to know WHEN the grid last changed subscribes to
/// <c>IScheduleStore.WeekChanged</c> instead of comparing snapshots against one
/// another — this type carries no version/generation counter of its own (removed as speculative
/// generality: no consumer has ever needed to compare two snapshots for staleness).
/// </summary>
public sealed record ScheduleWeekSnapshot(IReadOnlyList<ScheduleSegment> Segments);
