namespace GenWave.Core.Domain;

/// <summary>
/// Provenance of a <c>station.persona_memory</c> row (SPEC F71.1, F71.6; STORY-194).
/// <see cref="Authored"/> rows are operator hand-written bits/callbacks/notes and are exempt from
/// <see cref="Abstractions.IPersonaMemory"/>'s retention cap — never counted toward it, never evicted
/// by it. <see cref="Accrued"/> rows are whatever the DJ itself records as it airs bits and callbacks,
/// and are the only rows the cap governs.
/// </summary>
public enum PersonaMemorySource
{
    Authored,
    Accrued,
}
