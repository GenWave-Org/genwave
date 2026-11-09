namespace GenWave.Host.Tests.Fakes;

using GenWave.Core.Abstractions;

/// <summary>In-memory <see cref="IQuirkSpendLedger"/> that keeps every spend row it was asked to write.</summary>
sealed class RecordingQuirkLedger : IQuirkSpendLedger
{
    public List<string> Spends { get; } = [];

    public Task<int> CountSpendsAsync(long personaId, string content, CancellationToken ct) =>
        Task.FromResult(Spends.Count(s => s == content));

    public Task RecordSpendAsync(long personaId, string content, CancellationToken ct)
    {
        Spends.Add(content);
        return Task.CompletedTask;
    }
}
