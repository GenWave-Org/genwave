namespace GenWave.Core.Domain;

/// <summary>How a recall block orders its eligible rows (SPEC F218.22).</summary>
public enum MemoryRecallOrder
{
    /// <summary>By when the row happened (falling back to creation), most recent first.</summary>
    NewestFirst,

    /// <summary>Never-aired rows first, then the longest since aired, so a few rows do not monopolise prompts.</summary>
    LeastRecentlyAired,
}
