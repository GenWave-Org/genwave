namespace GenWave.Core.Domain;

/// <summary>
/// Outcome of <see cref="Abstractions.IScheduleStore.AssignShowAsync"/> (SPEC F119.2, STORY-313,
/// PLAN T243). Mirrors <see cref="ScheduleReplaceResult"/>'s closed-hierarchy shape: the private
/// constructor on the abstract base closes the hierarchy so callers can write exhaustive
/// pattern-match switches without a discard arm.
/// </summary>
public abstract record ShowAssignResult
{
    private ShowAssignResult() { }

    /// <summary>
    /// The write landed: <see cref="UpdatedBlockIds"/> names every <c>segment_schedule</c> row whose
    /// <c>show_id</c> this call actually set (or cleared, for a null target show) — the requested
    /// block alone when narrowed, or every row of its contiguous same-persona run otherwise (the
    /// F119.2 span rule; see <see cref="Abstractions.IScheduleStore.AssignShowAsync"/>'s own remarks
    /// for the exact rule).
    /// </summary>
    /// <param name="Version">The stored week's <see cref="ScheduleWeekVersion"/> content fingerprint,
    /// recomputed from the post-write rows INSIDE the same transaction as the write itself (mirrors
    /// <see cref="ScheduleReplaceResult.Replaced"/>'s own "the returned snapshot reflects exactly what
    /// this call wrote" discipline) — so a client that re-renders off this response and treats
    /// <see cref="Version"/> as its next <c>PUT /api/schedule</c>'s <c>BaseVersion</c> compares cleanly
    /// against the store: this call's own write already counts as "this editor's latest known state,"
    /// the same way a fresh <c>GET /api/schedule</c> would.</param>
    public sealed record Assigned(IReadOnlyList<long> UpdatedBlockIds, string Version) : ShowAssignResult;

    /// <summary>No <c>segment_schedule</c> row with the requested block id exists. Nothing was written.</summary>
    public sealed record BlockNotFound : ShowAssignResult;

    /// <summary>The requested target show id names no <c>station.show</c> row. Nothing was written —
    /// checked inside the same transaction as the write itself, so a show deleted between this check
    /// and the write can never leave a dangling reference behind either.</summary>
    public sealed record ShowNotFound : ShowAssignResult;
}
