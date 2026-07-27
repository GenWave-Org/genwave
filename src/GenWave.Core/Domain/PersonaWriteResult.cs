namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of an <see cref="Abstractions.IPersonaStore"/> write
/// (SPEC F35.1, F35.4, F91.9; STORY-118). Mirrors <see cref="LibraryWriteResult"/>'s closed-hierarchy
/// shape: cases that carry data (<see cref="Created"/>, <see cref="Updated"/>) are sealed records with
/// a positional <see cref="Persona"/> payload; singleton cases (<see cref="Deleted"/>,
/// <see cref="NotFound"/>, <see cref="NameConflict"/>, <see cref="ScheduledElsewhere"/>) carry none.
/// The private constructor on the abstract base closes the hierarchy so callers can write exhaustive
/// pattern-match switches without a discard arm.
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

    /// <summary>
    /// The delete was rejected because <c>station.segment_schedule.persona_id</c> still names this
    /// persona (the FK's own <c>ON DELETE RESTRICT</c>, SPEC F91.9) — mapped from the store's own
    /// caught <c>foreign_key_violation</c> (SQLSTATE 23503) rather than a raw <c>PostgresException</c>
    /// ever reaching a caller (PLAN T120 review F4, mirrors <see cref="NameConflict"/>'s own
    /// unique_violation mapping). PLAN T121 is expected to carry a payload naming the offending
    /// day/time slots; this case stays a plain singleton until then.
    /// </summary>
    public sealed record ScheduledElsewhere : PersonaWriteResult;
}
