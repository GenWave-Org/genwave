namespace GenWave.Core.Domain;

/// <summary>
/// Whether the prompt may carry DJ memory (SPEC F218.21). <see cref="MemoryOn"/> gates the memory
/// blocks and never-mention list (F218.22); it does NOT gate the recent-lines block, which is always
/// on (F218.37). T700/T701 extend it with per-block limits.
/// </summary>
/// <param name="RecentLines">Legacy: no longer drives the recent-lines block (F218.37); only tools/PatterBench still reads it.</param>
/// <param name="MemoryOn">True when the model's context window clears <see cref="MinContextTokens"/>.</param>
public sealed record MemoryBudget(int RecentLines, bool MemoryOn = false)
{
    /// <summary>The smallest context window (tokens) that still leaves room for memory blocks (SPEC F218.21).</summary>
    public const int MinContextTokens = 8192;

    /// <summary>No memory in the prompt.</summary>
    public static MemoryBudget Off { get; } = new(0);

    /// <summary>Memory on.</summary>
    public static MemoryBudget Standard { get; } = new(6, MemoryOn: true);

    /// <summary>Memory on only for a known window of at least <see cref="MinContextTokens"/>; unknown is off.</summary>
    public static MemoryBudget ForContextWindow(int? tokens) =>
        tokens is >= MinContextTokens ? Standard : Off;
}
