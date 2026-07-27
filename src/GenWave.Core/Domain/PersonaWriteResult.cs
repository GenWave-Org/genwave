namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of an <see cref="Abstractions.IPersonaStore"/> write
/// (SPEC F35.1, F35.4, F91.9; STORY-118). Mirrors <see cref="LibraryWriteResult"/>'s closed-hierarchy
/// shape: cases that carry data (<see cref="Created"/>, <see cref="Updated"/>,
/// <see cref="ScheduledElsewhere"/>) are sealed records with a positional payload; singleton cases
/// (<see cref="Deleted"/>, <see cref="NotFound"/>, <see cref="NameConflict"/>) carry none. The
/// private constructor on the abstract base closes the hierarchy so callers can write exhaustive
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
    /// persona (the FK's own <c>ON DELETE RESTRICT</c>, SPEC F91.9). <see cref="Slots"/> names every
    /// offending row, in day-then-start-minute order — PLAN T121 replaces the T120 scaffolding's bare
    /// singleton with this payload so <c>PersonaController.Delete</c>'s 409 body can name the slots
    /// instead of staying generic. The common path: the store's <c>DeleteAsync</c> queries
    /// <c>station.segment_schedule</c> BEFORE attempting the delete and returns exactly what it
    /// found. The race backstop: a slot painted between that query and the DELETE still trips the
    /// FK (caught <c>foreign_key_violation</c>, SQLSTATE 23503, mirrors <see cref="NameConflict"/>'s
    /// own unique_violation mapping) — that path re-queries and returns whatever it finds, which may
    /// be empty if the race closed the other way (the painted slot was itself removed again before
    /// the re-query). See that method's own remarks for the full rationale.
    /// </summary>
    public sealed record ScheduledElsewhere(IReadOnlyList<ScheduledSlot> Slots) : PersonaWriteResult;
}
