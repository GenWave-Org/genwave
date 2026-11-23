using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>An utterance store that only answers <see cref="RecentAsync"/>, newest first, like the real one.</summary>
internal sealed class SpokenRowsStore(IReadOnlyList<UtteranceEntry> rows) : IUtteranceStore
{
    public Task<IReadOnlyList<UtteranceEntry>> RecentAsync(long personaId, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UtteranceEntry>>(rows.Where(row => row.PersonaId == personaId).Take(take).ToList());

    public Task<long> RecordAsync(NewUtterance utterance, CancellationToken ct) => throw new NotSupportedException();

    public Task MarkAiredAsync(long id, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<UtteranceEntry>> SinceAsync(long personaId, DateTimeOffset since, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<int> SweepAsync(CancellationToken ct) => throw new NotSupportedException();
}
