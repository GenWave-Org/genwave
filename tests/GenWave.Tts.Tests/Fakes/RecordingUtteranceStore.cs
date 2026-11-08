namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// In-memory <see cref="IUtteranceStore"/> double (STORY-532): remembers every <see cref="NewUtterance"/>
/// handed to <see cref="RecordAsync"/> and answers ids 1, 2, 3, ... in order.
/// </summary>
public sealed class RecordingUtteranceStore : IUtteranceStore
{
    readonly List<NewUtterance> recorded = [];

    public IReadOnlyList<NewUtterance> Recorded => recorded;

    public Task<long> RecordAsync(NewUtterance utterance, CancellationToken ct)
    {
        recorded.Add(utterance);
        return Task.FromResult((long)recorded.Count);
    }

    public Task MarkAiredAsync(long id, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<UtteranceEntry>> RecentAsync(long personaId, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UtteranceEntry>>([]);

    public Task<int> SweepAsync(CancellationToken ct) => Task.FromResult(0);
}
