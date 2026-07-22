using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F35.1, STORY-118) — CRUD access to <c>station.persona</c>: the DJ persona
/// (backstory/style/voice) profiles a future orchestrator task blends into TTS patter. No DI
/// registration and no consumer land with this seam — a later Epic T task wires both.
/// </summary>
public interface IPersonaStore
{
    /// <summary>Returns every persona row, ordered by name.</summary>
    Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct);

    /// <summary>Returns the persona identified by <paramref name="id"/>, or null if no such row exists.</summary>
    Task<Persona?> GetByIdAsync(long id, CancellationToken ct);

    /// <summary>
    /// Creates a new persona from <paramref name="draft"/>. Returns
    /// <see cref="PersonaWriteResult.Created"/> with the new row on success, or
    /// <see cref="PersonaWriteResult.NameConflict"/> if a persona with that name already exists
    /// (SPEC F35.4 — enforced by the DB's <c>UNIQUE(name)</c> constraint, not a pre-read).
    /// </summary>
    Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct);

    /// <summary>
    /// Updates the persona identified by <paramref name="id"/> with <paramref name="draft"/>'s
    /// fields. Returns <see cref="PersonaWriteResult.Updated"/> with the row after the write
    /// (<c>updated_at</c> advanced) on success, <see cref="PersonaWriteResult.NotFound"/> if no such
    /// persona exists, or <see cref="PersonaWriteResult.NameConflict"/> if another persona already
    /// holds the requested name.
    /// </summary>
    Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct);

    /// <summary>
    /// Deletes the persona identified by <paramref name="id"/>. Returns
    /// <see cref="PersonaWriteResult.Deleted"/> on success, or <see cref="PersonaWriteResult.NotFound"/>
    /// if no such persona exists.
    /// </summary>
    Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct);

    /// <summary>
    /// Returns the persona-card definition for <paramref name="id"/> (SPEC F71.1, F71.3, F71.7) —
    /// the quirks/corrections/soul document a <see cref="Persona"/> row's <c>definition</c> column
    /// carries, distinct from <see cref="GetByIdAsync"/>'s legacy backstory/style/voice shape. Null
    /// when no such row exists, or when the row still carries the schema-migration sentinel
    /// (<c>'{}'::jsonb</c>, not yet reconciled by <c>PersonaCardMigrator</c>) — a real card is
    /// either fully present or not returned at all, never a half-populated one.
    /// </summary>
    Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct);

    /// <summary>
    /// Resolves a persona's id from its <c>slug</c> (SPEC F71.1's <c>UNIQUE(slug)</c> column) — the
    /// primitive the export/import routes (F79.1, F79.3; STORY-208/209) need before they can read
    /// <c>persona_memory</c>/<c>persona_taste</c>, both of which key off the numeric id, never the
    /// slug. Null when no persona holds that slug.
    /// </summary>
    Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct);
}
