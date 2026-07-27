namespace GenWave.Core.Domain;

/// <summary>
/// The four ways one row of a <see cref="Abstractions.IScheduleStore.ReplaceWeekAsync"/> submission
/// can fail application-side validation (SPEC F91.1, F91.8; STORY-240) before any statement reaches
/// <c>station.segment_schedule</c>.
/// </summary>
public enum ScheduleCellErrorKind
{
    /// <summary><see cref="ScheduleSegment.Day"/> is not a defined <see cref="System.DayOfWeek"/> value.</summary>
    InvalidDay,

    /// <summary><see cref="ScheduleSegment.StartMinute"/>/<see cref="ScheduleSegment.EndMinute"/> is off the
    /// 30-minute grid, out of range, or <c>EndMinute</c> does not exceed <c>StartMinute</c>.</summary>
    InvalidMinuteRange,

    /// <summary>The row's range intersects another row on the same day.</summary>
    Overlap,

    /// <summary><see cref="ScheduleSegment.PersonaId"/> names no row in <c>station.persona</c>.</summary>
    UnknownPersona,
}
