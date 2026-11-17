using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F218.22, STORY-539) — picks what one break's prompt remembers within its
/// <see cref="MemoryBudget"/>: 2 lore, 3 episodes (newest first, none aired within 48 h) and 3 life
/// rows, plus the never-mention list. Recalled rows are marked aired before this returns (F71.5), so
/// the caller dispatches the render afterwards.
/// </summary>
public interface IMemoryRecallSource
{
    /// <summary>A budget with memory off returns <see cref="MemoryPacket.Empty"/> and marks nothing.</summary>
    Task<MemoryPacket> TakeAsync(long personaId, MemoryBudget budget, CancellationToken ct);

    /// <summary>
    /// Only the never-mention list (F218.19), in a packet with no rows. Marks nothing: since F218.40 only a callback
    /// ask carries a memory, and that memory's rest is the callback store's to keep. A budget with memory off returns
    /// <see cref="MemoryPacket.Empty"/>. The on-air writer calls this; <see cref="TakeAsync"/> has no caller left (T737 deletes it).
    /// </summary>
    Task<MemoryPacket> NeverMentionAsync(long personaId, MemoryBudget budget, CancellationToken ct);
}
