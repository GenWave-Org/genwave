namespace GenWave.Core.Domain;

/// <summary>
/// What one break's prompt must avoid (SPEC F218.19): the never-mention list. Memory rows no longer ride
/// here; since F218.40 only a callback ask carries one memory.
/// </summary>
/// <param name="NeverMention">Claim texts forgotten within the last 7 days, newest first.</param>
public sealed record MemoryPacket(IReadOnlyList<string> NeverMention)
{
    /// <summary>Nothing to avoid.</summary>
    public static MemoryPacket Empty { get; } = new([]);
}
