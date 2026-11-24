using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IUtteranceStore"/> at the Postgres edge. <see cref="RecentAsync"/> and <see cref="SinceAsync"/> answer from
/// separate seeded lists, so a spec can place a row only the hour-window read finds (the two reads can differ by a row in production).
/// </summary>
sealed class SeededUtteranceStore(
    IReadOnlyList<UtteranceEntry>? recent = null, IReadOnlyList<UtteranceEntry>? inWindow = null) : IUtteranceStore
{
    readonly List<NewUtterance> recorded = [];

    public IReadOnlyList<NewUtterance> Recorded { get { lock (recorded) return [.. recorded]; } }

    public Task<long> RecordAsync(NewUtterance utterance, CancellationToken ct)
    {
        lock (recorded)
        {
            recorded.Add(utterance);
            return Task.FromResult((long)recorded.Count);
        }
    }

    public Task MarkAiredAsync(long id, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<UtteranceEntry>> RecentAsync(long personaId, int take, CancellationToken ct) =>
        Task.FromResult(recent ?? []);

    public Task<IReadOnlyList<UtteranceEntry>> SinceAsync(long personaId, DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult(inWindow ?? []);

    public Task<int> SweepAsync(CancellationToken ct) => Task.FromResult(0);
}
