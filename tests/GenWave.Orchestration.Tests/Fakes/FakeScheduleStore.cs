using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IScheduleStore"/> double (STORY-241, PLAN T119) that counts
/// <see cref="LoadWeekAsync"/> calls so a spec can pin "the feeder tick path issues no schedule store
/// query" (SPEC F91.3) structurally, and exposes <see cref="RaiseWeekChanged"/> to simulate a write from
/// another caller. <see cref="ReplaceWeekAsync"/> is not needed by any T119 spec — <see cref="CachingScheduleResolver"/>
/// only ever reads.
///
/// <para>
/// <see cref="ArmGate"/>/<see cref="ReleaseGate"/> (PLAN T119 review F4) let a spec hold a
/// <see cref="LoadWeekAsync"/> call incomplete so it can fire <see cref="RaiseWeekChanged"/> WHILE that
/// load is still in flight — reproducing the race where an invalidation lands mid-read.
/// <see cref="SetSnapshot"/> then models the write that triggered it having already landed in the store,
/// so the NEXT (ungated) load observes it.
/// </para>
/// </summary>
sealed class FakeScheduleStore(ScheduleWeekSnapshot snapshot) : IScheduleStore
{
    ScheduleWeekSnapshot current = snapshot;
    TaskCompletionSource<ScheduleWeekSnapshot>? pendingLoad;

    public int LoadWeekAsyncCallCount { get; private set; }

    public event Action? WeekChanged;

    public Task<ScheduleWeekSnapshot> LoadWeekAsync(CancellationToken ct)
    {
        LoadWeekAsyncCallCount++;
        return pendingLoad?.Task ?? Task.FromResult(current);
    }

    public Task<ScheduleReplaceResult> ReplaceWeekAsync(IReadOnlyList<ScheduleSegment> week, CancellationToken ct) =>
        throw new NotSupportedException("FakeScheduleStore is a read-only double for T119 specs.");

    public void RaiseWeekChanged() => WeekChanged?.Invoke();

    /// <summary>Arms a gate so the NEXT <see cref="LoadWeekAsync"/> call returns an incomplete task,
    /// held open until <see cref="ReleaseGate"/> completes it.</summary>
    public void ArmGate() => pendingLoad = new TaskCompletionSource<ScheduleWeekSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes the currently-armed gated load with <paramref name="result"/>.</summary>
    public void ReleaseGate(ScheduleWeekSnapshot result)
    {
        pendingLoad?.TrySetResult(result);
        pendingLoad = null;
    }

    /// <summary>What the next (non-gated) <see cref="LoadWeekAsync"/> call returns.</summary>
    public void SetSnapshot(ScheduleWeekSnapshot next) => current = next;
}
