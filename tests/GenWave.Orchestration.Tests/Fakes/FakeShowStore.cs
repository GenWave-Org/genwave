using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IShowStore"/> double (PLAN T360 review HIGH-1) — the show-identity sibling of
/// this project's own <see cref="FakeScheduleStore"/>/<see cref="FakeScheduleSpecialStore"/>: exposes
/// <see cref="RaiseShowChanged"/> so a spec can simulate an operator's show edit landing through the
/// REAL <c>ShowRepository</c> while <see cref="CachingScheduleResolver"/> is wired against a fake
/// schedule store instead. <see cref="CachingScheduleResolver"/> never reads through this seam (a
/// cached block's own <c>ShowSummary</c> rides <see cref="IScheduleStore.LoadWeekAsync"/>'s own join,
/// never a per-show lookup) — it only ever subscribes to the event — so every other member is a bare
/// <see cref="NotSupportedException"/>, mirroring <see cref="FakeScheduleStore"/>'s own read-only
/// posture for the members its own specs never exercise.
/// </summary>
sealed class FakeShowStore : IShowStore
{
    public event Action? ShowChanged;

    public void RaiseShowChanged() => ShowChanged?.Invoke();

    public Task<IReadOnlyList<Show>> GetAllAsync(CancellationToken ct) =>
        throw new NotSupportedException("FakeShowStore is an event-only double for CachingScheduleResolver specs.");

    public Task<Show?> GetByIdAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("FakeShowStore is an event-only double for CachingScheduleResolver specs.");

    public Task<Show?> GetBySlugAsync(string slug, CancellationToken ct) =>
        throw new NotSupportedException("FakeShowStore is an event-only double for CachingScheduleResolver specs.");

    public Task<ShowWriteResult> CreateAsync(ShowDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("FakeShowStore is an event-only double for CachingScheduleResolver specs.");

    public Task<ShowWriteResult> UpdateAsync(long id, ShowDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("FakeShowStore is an event-only double for CachingScheduleResolver specs.");

    public Task<ShowWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("FakeShowStore is an event-only double for CachingScheduleResolver specs.");

    public Task<Show?> ImportAsync(string slug, string name, string? tagline, string? flavor, string importedFrom, CancellationToken ct) =>
        throw new NotSupportedException("FakeShowStore is an event-only double for CachingScheduleResolver specs.");

    public Task<ShowWriteResult> SetRotationAsync(long id, RotationPredicate? rotation, CancellationToken ct) =>
        throw new NotSupportedException("FakeShowStore is an event-only double for CachingScheduleResolver specs.");
}
