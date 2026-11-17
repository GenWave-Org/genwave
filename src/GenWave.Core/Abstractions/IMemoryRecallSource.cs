using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F218.19, STORY-539) — what one break's prompt must avoid within its <see cref="MemoryBudget"/>:
/// the never-mention list. Since F218.40 no recall path carries memory rows; only a callback ask does.
/// </summary>
public interface IMemoryRecallSource
{
    /// <summary>
    /// The never-mention list (F218.19). Marks nothing. A budget with memory off returns
    /// <see cref="MemoryPacket.Empty"/>.
    /// </summary>
    Task<MemoryPacket> NeverMentionAsync(long personaId, MemoryBudget budget, CancellationToken ct);
}
