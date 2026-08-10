using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F115.1, STORY-305, PLAN T239) — CRUD access to <c>station.show</c>: the named-show
/// identity package (name/tagline/flavor/provenance) an hour of airtime can carry. Deliberately never
/// maps, reads, or writes <c>persona_id</c>/<c>envelope</c> (SPEC F115.2 — a law of the epic, not an
/// oversight); a future schedulable-bundle slice adds that seam separately. No DI registration and no
/// consumer land with this seam — <c>/api/shows</c> (PLAN T240) is the first.
/// </summary>
public interface IShowStore
{
    /// <summary>Returns every show row, ordered by name.</summary>
    Task<IReadOnlyList<Show>> GetAllAsync(CancellationToken ct);

    /// <summary>Returns the show identified by <paramref name="id"/>, or null if no such row exists.</summary>
    Task<Show?> GetByIdAsync(long id, CancellationToken ct);

    /// <summary>Returns the show identified by <paramref name="slug"/>, or null if no such row exists
    /// — the primitive a slug-addressed route (<c>/api/shows/{slug}</c>) resolves through.</summary>
    Task<Show?> GetBySlugAsync(string slug, CancellationToken ct);

    /// <summary>
    /// Creates a new authored show from <paramref name="draft"/>: <c>slug</c> is derived from
    /// <paramref name="draft"/>'s <c>Name</c> via the house Slugify, <c>imported_from</c>/
    /// <c>imported_at</c> stay null. Returns <see cref="ShowWriteResult.Created"/> with the new row on
    /// success, <see cref="ShowWriteResult.InvalidName"/> if <c>Name</c> is blank/whitespace-only or
    /// its Slugify output equals the fallback literal <c>"persona"</c> — whether by Slugify's own
    /// empty-slug rescue (an emoji-only name) or the ordinary path landing on that same string (e.g.
    /// the literal name <c>"Persona"</c> itself; see <see cref="ShowWriteResult.InvalidName"/>'s own
    /// remarks, PLAN T240 review A1), <see cref="ShowWriteResult.BudgetExceeded"/> if a field exceeds its SPEC F115.1 1×
    /// budget (checked before the write ever reaches Postgres), or
    /// <see cref="ShowWriteResult.SlugConflict"/> if the derived slug collides with an existing show
    /// (enforced by the DB's <c>UNIQUE(slug)</c>, not a pre-read).
    /// </summary>
    Task<ShowWriteResult> CreateAsync(ShowDraft draft, CancellationToken ct);

    /// <summary>
    /// Updates the show identified by <paramref name="id"/> with <paramref name="draft"/>'s fields —
    /// re-derives <c>slug</c> from the new <c>Name</c> the same way <see cref="CreateAsync"/> does, and
    /// never touches <c>imported_from</c>/<c>imported_at</c>. Returns <see cref="ShowWriteResult.Updated"/>
    /// with the row after the write (<c>updated_at</c> advanced) on success,
    /// <see cref="ShowWriteResult.NotFound"/> if no such show exists,
    /// <see cref="ShowWriteResult.InvalidName"/>/<see cref="ShowWriteResult.BudgetExceeded"/> the same
    /// way <see cref="CreateAsync"/> does, or <see cref="ShowWriteResult.SlugConflict"/> if another show
    /// already holds the re-derived slug.
    /// </summary>
    Task<ShowWriteResult> UpdateAsync(long id, ShowDraft draft, CancellationToken ct);

    /// <summary>
    /// Deletes the show identified by <paramref name="id"/>. Returns
    /// <see cref="ShowWriteResult.Deleted"/> on success, <see cref="ShowWriteResult.NotFound"/> if no
    /// such show exists, or <see cref="ShowWriteResult.Referenced"/> if
    /// <c>station.segment_schedule.show_id</c> still names it (the FK's own <c>ON DELETE RESTRICT</c>,
    /// caught as SQLSTATE 23503) — this seam's own case carries no detail beyond "referenced". Naming
    /// the format-clock blocks (and scoped imaging rows) a still-referenced show cannot be deleted
    /// through is the endpoint-layer guard PLAN T240 builds on top of that case.
    /// </summary>
    Task<ShowWriteResult> DeleteAsync(long id, CancellationToken ct);
}
