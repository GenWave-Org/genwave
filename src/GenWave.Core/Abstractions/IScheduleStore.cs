using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F91.1, F91.3, F91.8; STORY-240, STORY-242; PLAN T118) — CRUD access to
/// <c>station.segment_schedule</c>, the weekly format-clock grid that replaces the single
/// owner-toggled <c>Station:Persona:ActiveId</c>. No DI registration wires a Host call site and no
/// consumer lands with this seam yet — mirrors <see cref="IPersonaTasteStore"/>'s own original shape
/// ("ships dark"): <c>ScheduleResolver</c> (T119) and the <c>GET/PUT /api/schedule</c> endpoint (T122)
/// are later tasks.
/// </summary>
public interface IScheduleStore
{
    /// <summary>
    /// Returns every row in <c>station.segment_schedule</c>, ordered by day then start minute,
    /// wrapped in a <see cref="ScheduleWeekSnapshot"/>. An empty grid returns an empty list — the
    /// pre-clock, no-active-persona, 24/7-music-only state (SPEC F91.4).
    /// </summary>
    Task<ScheduleWeekSnapshot> LoadWeekAsync(CancellationToken ct);

    /// <summary>
    /// Replaces the ENTIRE week in one transaction: every existing row is deleted and
    /// <paramref name="week"/> is inserted in its place, or nothing changes at all. Application-side
    /// validation (30-minute step, in-range minutes, end greater than start, no two rows on the same
    /// day overlapping, every non-null persona id names a real row) runs BEFORE any statement reaches
    /// the database — a rejected submission returns <see cref="ScheduleReplaceResult.ValidationFailed"/>
    /// with one <see cref="ScheduleCellError"/> per offending row and leaves the stored week
    /// unchanged. The database's own CHECK/EXCLUDE/FK constraints (SPEC F91.1) remain the last line of
    /// defense, never the first — but that line can still fire: a persona named by a validated row can
    /// be deleted by a concurrent caller between this method's validation query and its insert, in
    /// which case the FK raises and this method throws <c>Npgsql.PostgresException</c> rather than
    /// returning <see cref="ScheduleReplaceResult.ValidationFailed"/>. Callers (T122's
    /// <c>PUT /api/schedule</c> handler) must treat that as an unexpected-error response and never echo
    /// the raw Postgres message to the client.
    ///
    /// <para>
    /// <paramref name="expectedVersion"/> (gh-#255): the <see cref="ScheduleWeekVersion"/> fingerprint
    /// of the week the caller believes it is replacing, or <see langword="null"/> to skip the check
    /// (legacy callers). Non-null and no longer matching the stored week — someone else replaced it
    /// since the caller loaded — returns <see cref="ScheduleReplaceResult.VersionConflict"/> and
    /// writes nothing: a full-replace built from stale state is exactly the silent operator-work
    /// wipe this guard exists to stop. The comparison runs inside the same transaction as the
    /// delete-then-insert.
    /// </para>
    /// </summary>
    Task<ScheduleReplaceResult> ReplaceWeekAsync(
        IReadOnlyList<ScheduleSegment> week, string? expectedVersion, CancellationToken ct);

    /// <summary>
    /// Raised synchronously right after a successful <see cref="ReplaceWeekAsync"/> commit — the
    /// change-notification seam a future in-memory cache (the T119 <c>ScheduleResolver</c>) subscribes
    /// to for invalidation, so the 3s feeder tick never has to poll Postgres for schedule changes
    /// (SPEC F91.3). Never raised when the write is rejected as
    /// <see cref="ScheduleReplaceResult.ValidationFailed"/>.
    /// </summary>
    event Action? WeekChanged;

    /// <summary>
    /// Every <c>station.segment_schedule</c> row naming <paramref name="showId"/>, ordered by day
    /// then start minute — the show delete guard's own detail read (SPEC F115.4, PLAN T240):
    /// <see cref="ShowWriteResult.Referenced"/> stays a bare singleton at the store seam (see
    /// its own remarks), so <c>ShowsController.Delete</c> calls this directly to NAME the blocking
    /// slots in its 409 body, mirroring <c>PersonaRepository.DeleteAsync</c>'s own pre-T121
    /// query-for-detail shape but at the endpoint layer instead — <c>IShowStore</c> never pre-queries
    /// this table itself (PLAN T239's own deliberate choice). An empty result means nothing in
    /// <c>station.segment_schedule</c> currently names this show.
    /// </summary>
    Task<IReadOnlyList<ScheduledSlot>> GetSlotsByShowIdAsync(long showId, CancellationToken ct);
}
