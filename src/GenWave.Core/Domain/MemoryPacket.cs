namespace GenWave.Core.Domain;

/// <summary>
/// What one break's prompt may remember (SPEC F218.22, F218.19): the lore, episodes (newest first) and
/// life rows chosen under the <see cref="MemoryBudget"/>, plus the never-mention list. Every row in
/// the three blocks has already been marked aired.
/// </summary>
/// <param name="NeverMention">Claim texts forgotten within the last 7 days, newest first.</param>
public sealed record MemoryPacket(
    IReadOnlyList<RecalledMemory> Lore,
    IReadOnlyList<RecalledMemory> Episodes,
    IReadOnlyList<RecalledMemory> Life,
    IReadOnlyList<string> NeverMention)
{
    /// <summary>Nothing remembered, nothing to avoid.</summary>
    public static MemoryPacket Empty { get; } = new([], [], [], []);
}
