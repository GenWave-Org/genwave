using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>
/// Default <see cref="IMemoryRecallSource"/> (SPEC F218.19, F218.22). Reads eligible rows per block from
/// the <see cref="IMemoryRecallStore"/>, then marks every chosen row aired in one statement
/// before returning (F71.5). The 6 h, 48 h and 7 d windows are handed to the
/// store as durations so the database's single clock measures them.
/// </summary>
public sealed class MemoryRecallSource(IMemoryRecallStore store) : IMemoryRecallSource
{
    /// <summary>A recalled lore row is not offered again within this long: about once a shift (F218.22).</summary>
    public static readonly TimeSpan LoreRecallGap = TimeSpan.FromHours(6);

    /// <summary>A recalled episode or life row is not offered again within this long (F218.22).</summary>
    public static readonly TimeSpan EpisodeAndLifeRecallGap = TimeSpan.FromHours(48);

    /// <summary>A forgotten claim stays on the never-mention list this long (F218.19).</summary>
    public static readonly TimeSpan NeverMentionWindow = TimeSpan.FromDays(7);

    /// <summary>Most never-mention entries (F218.19).</summary>
    public const int NeverMentionLimit = 10;

    public async Task<MemoryPacket> TakeAsync(long personaId, MemoryBudget budget, CancellationToken ct)
    {
        if (!budget.MemoryOn)
            return MemoryPacket.Empty;

        var lore = await store.CandidatesAsync(personaId, new(MemoryKinds.Lore, budget.LoreLimit, LoreRecallGap, MemoryRecallOrder.LeastRecentlyAired), ct);
        var episodes = await store.CandidatesAsync(personaId, new(MemoryKinds.Episode, budget.EpisodeLimit, EpisodeAndLifeRecallGap, MemoryRecallOrder.NewestFirst), ct);
        var life = await store.CandidatesAsync(personaId, new(MemoryKinds.Life, budget.LifeLimit, EpisodeAndLifeRecallGap, MemoryRecallOrder.LeastRecentlyAired), ct);
        var neverMention = await store.RecentlyForgottenAsync(personaId, NeverMentionWindow, NeverMentionLimit, ct);

        // Mark before render (F71.5): the one atomic mark is awaited before the caller can dispatch.
        var packet = new MemoryPacket(lore, episodes, life, neverMention);
        var chosen = packet.Rows.Select(row => row.Id).ToList();
        if (chosen.Count > 0)
            await store.MarkAiredAsync(chosen, ct);

        return packet;
    }

    public async Task<MemoryPacket> NeverMentionAsync(long personaId, MemoryBudget budget, CancellationToken ct)
    {
        if (!budget.MemoryOn)
            return MemoryPacket.Empty;

        var neverMention = await store.RecentlyForgottenAsync(personaId, NeverMentionWindow, NeverMentionLimit, ct);
        return new MemoryPacket([], [], [], neverMention);
    }
}
