namespace GenWave.Core.Domain;

/// <summary>The <c>station.persona_memory.kind</c> values recall and the quirk ledger rely on (SPEC F218).</summary>
public static class MemoryKinds
{
    /// <summary>Authored or approved backstory; recalled up to <see cref="MemoryBudget.LoreLimit"/> per prompt.</summary>
    public const string Lore = "lore";

    /// <summary>Something that happened on air; recalled newest first.</summary>
    public const string Episode = "episode";

    /// <summary>A timeless fact about the DJ's off-air life.</summary>
    public const string Life = "life";

    /// <summary>A spent once-a-shift quirk (the quirk ledger's rows).</summary>
    public const string Quirk = "quirk";

    /// <summary>The kinds the operator sees and shapes; the quirk ledger's rows are not memory they edit.</summary>
    public static readonly IReadOnlyList<string> OperatorShaped = [Lore, Episode, Life];
}
