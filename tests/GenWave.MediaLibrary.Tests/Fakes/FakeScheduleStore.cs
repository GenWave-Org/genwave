using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Fixed-snapshot <see cref="IScheduleStore"/> double (SPEC F153.8; STORY-378; PLAN T376) —
/// <c>UnreachableGardenerPass</c>'s own ONE read (<see cref="LoadWeekAsync"/>) is the only member any
/// Story378 fact needs; every write member throws <see cref="NotSupportedException"/> since the pass
/// never calls them and a spec that accidentally did should fail loudly, not silently no-op.
/// </summary>
public sealed class FakeScheduleStore(IReadOnlyList<ScheduleSegment> segments) : IScheduleStore
{
    public Task<ScheduleWeekSnapshot> LoadWeekAsync(CancellationToken ct) =>
        Task.FromResult(new ScheduleWeekSnapshot(segments));

    public Task<ScheduleReplaceResult> ReplaceWeekAsync(
        IReadOnlyList<ScheduleSegment> week, string? expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("FakeScheduleStore is read-only — UnreachableGardenerPass never writes.");

    // Custom (non-field-like) accessors — a field-like event never raised anywhere in this type
    // trips CS0067 ("never used") under -warnaserror; UnreachableGardenerPass never subscribes.
    public event Action? WeekChanged
    {
        add { }
        remove { }
    }

    public Task<ShowAssignResult> AssignShowAsync(long blockId, long? showId, bool applyToRun, CancellationToken ct) =>
        throw new NotSupportedException("FakeScheduleStore is read-only — UnreachableGardenerPass never writes.");

    public Task<IReadOnlyList<ScheduledSlot>> GetSlotsByShowIdAsync(long showId, CancellationToken ct) =>
        throw new NotSupportedException("FakeScheduleStore is read-only — UnreachableGardenerPass never writes.");
}
