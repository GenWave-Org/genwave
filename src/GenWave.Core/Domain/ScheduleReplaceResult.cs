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

    /// <summary>
    /// A row this call had already validated named a persona (or, since <c>ValidateAsync</c> never
    /// checks show-id existence, a show) that a concurrent caller deleted between validation and this
    /// call's own insert — <c>station.segment_schedule</c>'s FK (<c>persona_id</c> or <c>show_id</c>,
    /// both <c>ON DELETE RESTRICT</c>) raises where <see cref="ValidationFailed"/> would otherwise have
    /// caught it. Nothing was written. <c>gh-#406 slice 1</c>: this mapping used to be
    /// <c>ScheduleController</c> catching <c>Npgsql.PostgresException</c> directly (an L2
    /// Postgres-confinement violation) — <c>GenWave.MediaLibrary.Station.ScheduleRepository</c> now
    /// catches the SQLSTATE itself and returns this case instead, so the controller (and everything
    /// outside the repository layer) never references Npgsql at all.
    /// </summary>
    public sealed record PersonaVanished : ScheduleReplaceResult;
}
