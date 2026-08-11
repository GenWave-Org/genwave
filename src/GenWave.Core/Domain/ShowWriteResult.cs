namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of an <see cref="Abstractions.IShowStore"/> write
/// (SPEC F115.1, STORY-305, PLAN T239). Mirrors <see cref="PersonaWriteResult"/>'s own closed-hierarchy
/// shape: cases that carry data (<see cref="Created"/>, <see cref="Updated"/>,
/// <see cref="BudgetExceeded"/>) are sealed records with a positional payload; singleton cases
/// (<see cref="Deleted"/>, <see cref="NotFound"/>, <see cref="SlugConflict"/>, <see cref="InvalidName"/>,
/// <see cref="Referenced"/>) carry none. The private constructor on the abstract base closes the
/// hierarchy so callers can write exhaustive pattern-match switches without a discard arm.
/// </summary>
public abstract record ShowWriteResult
{
    private ShowWriteResult() { }

    /// <summary>The show was created; <see cref="Show"/> is the new row.</summary>
    public sealed record Created(Show Show) : ShowWriteResult;

    /// <summary>The show was updated; <see cref="Show"/> is the row after the write (updated_at advanced).</summary>
    public sealed record Updated(Show Show) : ShowWriteResult;

    /// <summary>The show was successfully deleted.</summary>
    public sealed record Deleted : ShowWriteResult;

    /// <summary>No show with the requested id exists.</summary>
    public sealed record NotFound : ShowWriteResult;

    /// <summary>Another show already holds the derived slug (UNIQUE(slug), SPEC F115.1) — the unique
    /// constraint surfacing as a conflict, never a silent overwrite.</summary>
    public sealed record SlugConflict : ShowWriteResult;

    /// <summary>
    /// <see cref="Field"/> exceeds its SPEC F115.1 1× budget (name ≤60, tagline ≤120, flavor ≤400
    /// chars, <see cref="ShowBudgets"/>) — rejected at the app seam before the write ever reaches
    /// Postgres.
    /// </summary>
    public sealed record BudgetExceeded(ShowBudgetField Field) : ShowWriteResult;

    /// <summary>
    /// <c>Name</c> was blank/whitespace-only, or its house-Slugify output equals the fallback literal
    /// <c>"persona"</c> (<c>LegacyPersonaCardMapper.FallbackSlug</c>) — rejected regardless of HOW the
    /// slug got there: an emoji-only name that hits <c>Slugify</c>'s own empty-slug rescue, but also
    /// (PLAN T240 review A1) a name that slugifies to <c>"persona"</c> the ordinary way, e.g. the
    /// literal name <c>"Persona"</c> itself (lowercases unchanged — the rescue never fires; the
    /// ordinary path just lands on the same string). Rejected at the app seam before the write ever
    /// reaches Postgres. Mirrors <c>PersonaController</c>'s import-slug REJECT-not-autocorrect posture
    /// (never silently coerce a bad name into something plausible-looking), enforced here at the
    /// store seam so every future caller of <see cref="Abstractions.IShowStore"/> inherits the guard
    /// rather than each needing its own.
    /// </summary>
    public sealed record InvalidName : ShowWriteResult;

    /// <summary>
    /// The delete was rejected because <c>station.segment_schedule.show_id</c> still names this show
    /// — the FK's own <c>ON DELETE RESTRICT</c> (db/06, SPEC F114), caught here as SQLSTATE 23503.
    /// Unlike <see cref="PersonaWriteResult.ScheduledElsewhere"/>, this store does not pre-query the
    /// schedule for slot detail before deleting: this case stays a bare singleton, "referenced" and
    /// nothing more — PLAN T240's endpoint-layer guard (SPEC F115.4) is what names the offending
    /// blocks in the 409 body, the same way PLAN T121 once replaced
    /// <see cref="PersonaWriteResult.ScheduledElsewhere"/>'s own T120 scaffolding.
    /// </summary>
    public sealed record Referenced : ShowWriteResult;
}
