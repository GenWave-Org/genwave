namespace GenWave.Core.Domain;

/// <summary>
/// Outcome of <see cref="Abstractions.IScheduleStore.ReplaceWeekAsync"/> (SPEC F91.1, F91.8;
/// STORY-240, PLAN T118). Mirrors <see cref="PersonaWriteResult"/>'s closed-hierarchy shape: the
/// private constructor on the abstract base closes the hierarchy so callers can write exhaustive
/// pattern-match switches without a discard arm.
/// </summary>
public abstract record ScheduleReplaceResult
{
    private ScheduleReplaceResult() { }

    /// <summary>The whole week was replaced; <see cref="Snapshot"/> is the store's state immediately
    /// after the write (same shape <see cref="Abstractions.IScheduleStore.LoadWeekAsync"/> returns).</summary>
    public sealed record Replaced(ScheduleWeekSnapshot Snapshot) : ScheduleReplaceResult;

    /// <summary>At least one submitted row failed application-side validation — nothing was written;
    /// <see cref="Errors"/> holds one <see cref="ScheduleCellError"/> per offending row.</summary>
    public sealed record ValidationFailed(IReadOnlyList<ScheduleCellError> Errors) : ScheduleReplaceResult;

    /// <summary>The caller's <c>expectedVersion</c> no longer matches the stored week — someone else
    /// (another tab, another session) replaced it since the caller loaded — so nothing was written
    /// (gh-#255's stale-editor silent-wipe guard). <see cref="CurrentVersion"/> is the stored week's
    /// live <see cref="ScheduleWeekVersion"/> fingerprint at rejection time.</summary>
    public sealed record VersionConflict(string CurrentVersion) : ScheduleReplaceResult;
}
