namespace GenWave.Core.Domain;

/// <summary>The bound on one memory row's text, shared by the life extractor and the operator's lore (SPEC F218).</summary>
public static class MemoryText
{
    /// <summary>Longest text kept; a longer one is prose, not a fact.</summary>
    public const int MaxChars = 200;
}
