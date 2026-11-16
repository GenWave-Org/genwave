namespace GenWave.Core.Domain;

/// <summary>
/// One operator edit of a memory row, applied all together or not at all. <paramref name="Text"/> is the
/// already-trimmed new text; <paramref name="Approve"/> approves a drafted row; <paramref name="Forgotten"/>
/// true forgets the row, false un-forgets it, null leaves it.
/// </summary>
public sealed record PersonaMemoryChange(string? Text, bool Approve, bool? Forgotten);
