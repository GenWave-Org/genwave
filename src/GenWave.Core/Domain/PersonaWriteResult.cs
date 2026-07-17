namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of an <see cref="Abstractions.IPersonaStore"/> write
/// (SPEC F35.1, F35.4; STORY-118). Mirrors <see cref="LibraryWriteResult"/>'s closed-hierarchy shape:
/// cases that carry data (<see cref="Created"/>, <see cref="Updated"/>) are sealed records with a
/// positional <see cref="Persona"/> payload; singleton cases (<see cref="Deleted"/>,
/// <see cref="NotFound"/>, <see cref="NameConflict"/>) carry none. The private constructor on the
/// abstract base closes the hierarchy so callers can write exhaustive pattern-match switches without
/// a discard arm.
/// </summary>
public abstract record PersonaWriteResult
{
    private PersonaWriteResult() { }

    /// <summary>The persona was created; <see cref="Persona"/> is the new row.</summary>
    public sealed record Created(Persona Persona) : PersonaWriteResult;

    /// <summary>The persona was updated; <see cref="Persona"/> is the row after the write (updated_at advanced).</summary>
    public sealed record Updated(Persona Persona) : PersonaWriteResult;

    /// <summary>The persona was successfully deleted.</summary>
    public sealed record Deleted : PersonaWriteResult;

    /// <summary>No persona with the requested id exists.</summary>
    public sealed record NotFound : PersonaWriteResult;

    /// <summary>Another persona already holds the requested name (UNIQUE(name), F35.4).</summary>
    public sealed record NameConflict : PersonaWriteResult;
}
