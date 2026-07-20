namespace GenWave.Core.Domain;

/// <summary>
/// One row read back from <c>station.persona_memory</c> (SPEC F71.1, F71.4-F71.6; STORY-194) —
/// <see cref="Abstractions.IPersonaMemory.RecallAsync"/>'s projection. <see cref="LastAiredAt"/> is
/// <see langword="null"/> when the row has never aired.
/// </summary>
public sealed record PersonaMemoryEntry(
    long Id,
    long PersonaId,
    string Kind,
    string Content,
    PersonaMemorySource Source,
    int AiredCount,
    DateTime? LastAiredAt,
    DateTime CreatedAt);
