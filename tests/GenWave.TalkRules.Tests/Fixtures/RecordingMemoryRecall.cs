using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>Hands out a fixed never-mention list (or throws) and remembers each ask.</summary>
internal sealed class RecordingMemoryRecall(params string[] forgotten) : IMemoryRecallSource
{
    public List<(long PersonaId, MemoryBudget Budget)> Calls { get; } = [];

    public bool Throws { get; init; }

    public Task<MemoryPacket> NeverMentionAsync(long personaId, MemoryBudget budget, CancellationToken ct)
    {
        Calls.Add((personaId, budget));
        return Throws ? throw new InvalidOperationException("memory store down") : Task.FromResult(new MemoryPacket(forgotten));
    }
}
