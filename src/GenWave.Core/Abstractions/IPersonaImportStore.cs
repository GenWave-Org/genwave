using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F79.3, F79.6; STORY-209, PLAN T67) — the ONE transactional write path a persona-card
/// import uses. Deliberately separate from <see cref="IPersonaStore"/>/<see cref="IPersonaMemory"/>/
/// <see cref="IPersonaTasteStore"/>: those three are connection-per-query (each call opens and
/// commits its own connection), so smearing a <c>System.Transactions.TransactionScope</c> across all
/// three to get F79.6's "a rejected import changes nothing, a mid-import failure rolls back
/// everything" guarantee would mean ambient-transaction plumbing bleeding into seams that have
/// nothing else to do with import. Instead this seam's single implementation
/// (<c>GenWave.MediaLibrary.Station.PersonaImportRepository</c>) owns ONE connection and ONE
/// <c>NpgsqlTransaction</c> spanning <c>station.persona</c>'s upsert-by-slug AND the authored-row
/// replace of both <c>station.persona_memory</c> and <c>station.persona_taste</c> — every statement
/// commits together or none do.
/// </summary>
public interface IPersonaImportStore
{
    /// <summary>
    /// Imports <paramref name="request"/>'s card as one atomic unit (SPEC F79.3): upserts
    /// <c>station.persona</c> by slug (card fields replace the definition), then replaces ONLY the
    /// <c>source='authored'</c> rows of <c>persona_memory</c>/<c>persona_taste</c> for that persona
    /// with <see cref="PersonaCard.Lore"/>/<see cref="PersonaCard.Taste"/> — every accrued row (either
    /// table) is left untouched, by construction (the delete this method issues is scoped to
    /// <c>source = 'authored'</c>, never a bare "delete all for this persona"). Returns
    /// <see cref="PersonaImportOutcome.Imported"/> on success, or
    /// <see cref="PersonaImportOutcome.NameConflict"/> if another persona already holds
    /// <see cref="PersonaCard.Name"/> — station.persona's <c>UNIQUE(name)</c> constraint, caught
    /// rather than pre-checked, mirroring <c>PersonaRepository.CreateAsync</c>'s own convention.
    /// </summary>
    Task<PersonaImportOutcome> ImportAsync(PersonaImportRequest request, CancellationToken ct);
}
