namespace GenWave.Core.Domain;

/// <summary>
/// How much DJ memory the prompt may carry (SPEC F218.21). Zero everywhere means memory is off: no
/// recent-lines block. Later tasks (T699/T700) extend it.
/// </summary>
/// <param name="RecentLines">How many of the persona's newest utterances the prompt lists (F218.10).</param>
public sealed record MemoryBudget(int RecentLines)
{
    /// <summary>No memory in the prompt.</summary>
    public static MemoryBudget Off { get; } = new(0);

    /// <summary>The standard budget: the newest 6 utterances.</summary>
    public static MemoryBudget Standard { get; } = new(6);
}
