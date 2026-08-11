using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IScheduleSpecialStore"/> double (STORY-317, PLAN T259) for
/// <see cref="GenWave.Host.Api.SpecialsController"/>'s own wire-layer specs AND
/// <see cref="GenWave.Host.Api.ShowsController"/>'s delete-guard extension (that controller now
/// resolves <see cref="IScheduleSpecialStore"/> too — see its own class remarks) — mirrors
/// <c>FakeShowStore</c>/<c>FakeScheduleStore</c>'s own posture: this double is deliberately NOT a
/// re-implementation of <c>GenWave.MediaLibrary.Station.SpecialsRepository</c>'s own db/36 CHECK/
/// EXCLUDE/FK behavior, nor its <see cref="ScheduleSpecialCreateResult"/> SQLSTATE translation (both
/// proven for real, against a real Postgres fixture, in
/// <c>GenWave.MediaLibrary.Tests/Specs/Story317_SpecialsStore.cs</c>). The default (unscripted)
/// <see cref="CreateAsync"/> simply stores whatever is submitted (a fresh id assigned, wrapped in
/// <see cref="ScheduleSpecialCreateResult.Created"/>) — a scenario proving
/// <see cref="GenWave.Host.Api.SpecialsController"/>'s own overlap/race MAPPING scripts
/// <see cref="NextCreateResult"/> instead, exactly the way <c>FakeShowStore.NextCreateResult</c> lets
/// <c>Story305_ShowsApi.cs</c> prove <c>ShowsController</c>'s own <c>ShowWriteResult</c> mapping
/// without a real Postgres fixture in this project.
/// </summary>
sealed class FakeScheduleSpecialStore : IScheduleSpecialStore
{
    readonly Dictionary<long, ScheduleSpecial> byId;
    long nextId;

    /// <summary>Seeds the store with pre-existing rows.</summary>
    public FakeScheduleSpecialStore(IEnumerable<ScheduleSpecial>? seed = null)
    {
        byId = (seed ?? []).ToDictionary(s => s.Id ?? throw new InvalidOperationException("Seeded specials must carry an id."));
        nextId = byId.Count == 0 ? 1 : byId.Keys.Max() + 1;
    }

    /// <summary>Scripts the NEXT <see cref="CreateAsync"/> call's outcome verbatim, bypassing the
    /// default echo-and-store behavior — proves <see cref="GenWave.Host.Api.SpecialsController.Create"/>'s
    /// own <see cref="ScheduleSpecialCreateResult"/> mapping (409 for
    /// <see cref="ScheduleSpecialCreateResult.Overlap"/>/<see cref="ScheduleSpecialCreateResult.UnknownReference"/>)
    /// without a real Postgres fixture. Cleared after one use.</summary>
    public ScheduleSpecialCreateResult? NextCreateResult { get; set; }

    public event Action? SpecialsChanged;

    public Task<IReadOnlyList<ScheduleSpecial>> ListUpcomingAsync(DateOnly fromDate, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ScheduleSpecial>>(
            byId.Values
                .Where(s => s.OnDate >= fromDate)
                .OrderBy(s => s.OnDate)
                .ThenBy(s => s.StartMinute)
                .ToList());

    public Task<ScheduleSpecialCreateResult> CreateAsync(ScheduleSpecial special, CancellationToken ct)
    {
        if (NextCreateResult is { } scripted)
        {
            NextCreateResult = null;
            return Task.FromResult(scripted);
        }

        var id = nextId++;
        var created = special with { Id = id };
        byId[id] = created;
        SpecialsChanged?.Invoke();
        return Task.FromResult<ScheduleSpecialCreateResult>(new ScheduleSpecialCreateResult.Created(created));
    }

    public Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        if (!byId.Remove(id))
            return Task.FromResult(false);

        SpecialsChanged?.Invoke();
        return Task.FromResult(true);
    }
}
