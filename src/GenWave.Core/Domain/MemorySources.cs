namespace GenWave.Core.Domain;

/// <summary>The <c>station.persona_memory.source</c> texts the operator's view branches on (SPEC F218.20).</summary>
public static class MemorySources
{
    /// <summary>Lore a model proposed; stored with <c>approved_at</c> null and never recalled until approved.</summary>
    public const string Drafted = "drafted";

    /// <summary>Lore the operator wrote; recallable at once. Names the stored text for readers (the insert SQL spells it as a literal).</summary>
    public const string Authored = "authored";
}
