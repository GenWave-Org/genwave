namespace GenWave.Core.Domain;

/// <summary>
/// How much DJ memory the prompt may carry (SPEC F218.21). Gates the memory blocks and never-mention
/// list (F218.22); it does NOT gate the recent-lines block, which is always on (F218.37). Later tasks (T699/T700) extend it.
/// </summary>
/// <param name="RecentLines">Legacy: no longer drives the recent-lines block (F218.37); T699 reshapes this.</param>
public sealed record MemoryBudget(int RecentLines)
{
    /// <summary>No memory in the prompt.</summary>
    public static MemoryBudget Off { get; } = new(0);

    /// <summary>Placeholder until T699 reshapes the budget.</summary>
    public static MemoryBudget Standard { get; } = new(6);
}
