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
}
