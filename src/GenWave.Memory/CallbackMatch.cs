namespace GenWave.Memory;

/// <summary>Whether an aired line used a memory, and the memory's distinctive words the line shared (lowercased, as in the memory).</summary>
public sealed record CallbackMatch(bool Used, IReadOnlyList<string> Words);
