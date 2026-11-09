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

    /// <summary>What <see cref="RecentAsync"/> answers, newest first.</summary>
    public List<UtteranceEntry> Seeded { get; } = [];

    public Task<long> RecordAsync(NewUtterance utterance, CancellationToken ct)
    {
        recorded.Add(utterance);
        return Task.FromResult((long)recorded.Count);
    }

    public Task MarkAiredAsync(long id, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<UtteranceEntry>> RecentAsync(long personaId, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UtteranceEntry>>(Seeded.Where(e => e.PersonaId == personaId).Take(take).ToList());

    public Task<int> SweepAsync(CancellationToken ct) => Task.FromResult(0);
}
