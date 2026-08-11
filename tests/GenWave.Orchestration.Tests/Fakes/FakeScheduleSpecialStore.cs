using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IScheduleSpecialStore"/> double (STORY-317, PLAN T260) — the specials-cache
/// sibling of this project's own <see cref="FakeScheduleStore"/>: counts
/// <see cref="ListUpcomingAsync"/> calls so a spec can pin how often <see cref="CachingScheduleResolver"/>
/// actually reloads its specials cache, and exposes <see cref="RaiseSpecialsChanged"/> to simulate a
/// write from another caller (mirrors <see cref="FakeScheduleStore.RaiseWeekChanged"/>). Never
/// implements <see cref="CreateAsync"/>/<see cref="DeleteAsync"/> for real —
/// <see cref="CachingScheduleResolver"/> only ever reads (mirrors <see cref="FakeScheduleStore"/>'s own
/// read-only posture, and <c>GenWave.Host.Tests.Fakes.FakeScheduleSpecialStore</c>'s own remarks on why
/// a re-implementation of <c>SpecialsRepository</c>'s db/36 CHECK/EXCLUDE/FK behavior belongs only in
/// the real-Postgres-backed <c>GenWave.MediaLibrary.Tests</c> suite).
///
/// <para>
/// <see cref="ArmGate"/>/<see cref="ReleaseGate"/> (mirrors <see cref="FakeScheduleStore"/>'s own pair)
/// let a spec hold a <see cref="ListUpcomingAsync"/> call incomplete so it can fire
/// <see cref="RaiseSpecialsChanged"/> WHILE that load is still in flight, reproducing the same
/// mid-flight-invalidation race <see cref="FakeScheduleStore"/> already proves for the week snapshot.
/// </para>
/// </summary>
sealed class FakeScheduleSpecialStore(IReadOnlyList<ScheduleSpecial>? seed = null) : IScheduleSpecialStore
{
    IReadOnlyList<ScheduleSpecial> current = seed ?? [];
    TaskCompletionSource<IReadOnlyList<ScheduleSpecial>>? pendingLoad;

    public int ListUpcomingAsyncCallCount { get; private set; }

    /// <summary>Every <c>fromDate</c> a caller has ever passed, in call order — lets a spec assert
    /// EXACTLY what date <see cref="CachingScheduleResolver"/> asked for on a given reload (PLAN T260:
    /// "pass today, take what comes").</summary>
    public List<DateOnly> RequestedFromDates { get; } = [];

    /// <summary>Set to make the next <see cref="ListUpcomingAsync"/> call throw, mirroring
    /// <see cref="FakeScheduleStore.ThrowOnLoadWeek"/>'s own convention. Never cleared automatically.</summary>
    public Exception? ThrowOnListUpcoming { get; set; }

    public event Action? SpecialsChanged;

    public Task<IReadOnlyList<ScheduleSpecial>> ListUpcomingAsync(DateOnly fromDate, CancellationToken ct)
    {
        ListUpcomingAsyncCallCount++;
        RequestedFromDates.Add(fromDate);
        if (ThrowOnListUpcoming is { } ex) throw ex;

        return pendingLoad?.Task ?? Task.FromResult(current);
    }

    public Task<ScheduleSpecialCreateResult> CreateAsync(ScheduleSpecial special, CancellationToken ct) =>
        throw new NotSupportedException("FakeScheduleSpecialStore is a read-only double for T260 caching specs.");

    public Task<bool> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("FakeScheduleSpecialStore is a read-only double for T260 caching specs.");

    public void RaiseSpecialsChanged() => SpecialsChanged?.Invoke();

    /// <summary>Arms a gate so the NEXT <see cref="ListUpcomingAsync"/> call returns an incomplete task,
    /// held open until <see cref="ReleaseGate"/> completes it.</summary>
    public void ArmGate() => pendingLoad = new TaskCompletionSource<IReadOnlyList<ScheduleSpecial>>(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes the currently-armed gated load with <paramref name="result"/>.</summary>
    public void ReleaseGate(IReadOnlyList<ScheduleSpecial> result)
    {
        pendingLoad?.TrySetResult(result);
        pendingLoad = null;
    }

    /// <summary>What the next (non-gated) <see cref="ListUpcomingAsync"/> call returns.</summary>
    public void SetSpecials(IReadOnlyList<ScheduleSpecial> next) => current = next;
}
