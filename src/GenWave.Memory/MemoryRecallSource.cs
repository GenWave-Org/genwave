using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>
/// Default <see cref="IMemoryRecallSource"/> (SPEC F218.19): reads the recently forgotten claims from the
/// <see cref="IMemoryRecallStore"/>. The 7 d window is handed to the store as a duration so the
/// database's single clock measures it.
/// </summary>
public sealed class MemoryRecallSource(IMemoryRecallStore store) : IMemoryRecallSource
{
    /// <summary>A forgotten claim stays on the never-mention list this long (F218.19).</summary>
    public static readonly TimeSpan NeverMentionWindow = TimeSpan.FromDays(7);

    /// <summary>Most never-mention entries (F218.19).</summary>
    public const int NeverMentionLimit = 10;

    public async Task<MemoryPacket> NeverMentionAsync(long personaId, MemoryBudget budget, CancellationToken ct)
    {
        if (!budget.MemoryOn)
            return MemoryPacket.Empty;

        var neverMention = await store.RecentlyForgottenAsync(personaId, NeverMentionWindow, NeverMentionLimit, ct);
        return new MemoryPacket(neverMention);
    }
}
