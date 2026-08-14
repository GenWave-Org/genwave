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
    /// The delete was rejected because <c>station.segment_schedule.persona_id</c> and/or
    /// <c>station.schedule_special.persona_id</c> still name this persona (both FKs share the identical
    /// <c>ON DELETE RESTRICT</c>, SPEC F91.9, F120.1, db/36). <see cref="Slots"/> names every offending
    /// weekly row, in day-then-start-minute order; <see cref="Specials"/> (gh-#462, widening PLAN T121's
    /// own <see cref="ScheduledSlot"/>-only payload) names every offending dated special, in
    /// date-then-start-minute order — so <c>PersonaController.Delete</c>'s 409 body can name BOTH kinds
    /// of blocker instead of only ever the weekly grid. The common path: the store's <c>DeleteAsync</c>
    /// queries BOTH tables BEFORE attempting the delete and returns exactly what it found. The race
    /// backstop: a slot or special painted between that query and the DELETE still trips one of the two
    /// FKs (caught <c>foreign_key_violation</c>, SQLSTATE 23503, mirrors <see cref="NameConflict"/>'s
    /// own unique_violation mapping) — that path re-queries both tables and returns whatever it finds,
    /// which may leave both lists empty if the race closed the other way (the painted row was itself
    /// removed again before the re-query). See that method's own remarks for the full rationale.
    /// </summary>
    public sealed record ScheduledElsewhere(
        IReadOnlyList<ScheduledSlot> Slots, IReadOnlyList<ScheduledSpecialSlot> Specials) : PersonaWriteResult;
}
