using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F218.19, F218.20, F218.24) — the operator's read and edit access to one persona's memory rows.
/// Every member is scoped by BOTH the persona id and the row id, so a row of persona A can never be read or
/// changed through persona B's route. Members that name a row report not-found when no such row
/// belongs to that persona.
/// </summary>
public interface IPersonaMemoryAdmin
{
    /// <summary>
    /// The persona's lore, episode and life rows (the quirk ledger's rows are not memory the operator shapes),
    /// optionally narrowed to <paramref name="kind"/>, newest first.
    /// </summary>
    Task<IReadOnlyList<PersonaMemoryAdminRow>> ListForAdminAsync(long personaId, string? kind, CancellationToken ct);

    /// <summary>Adds one lore row, <c>source=authored</c>, recallable at once. Returns the stored row.</summary>
    Task<PersonaMemoryAdminRow> AddAuthoredLoreAsync(long personaId, string text, CancellationToken ct);

    /// <summary>
    /// Adds each text as one lore row, <c>source=drafted</c>, <c>approved_at</c> null (pending, never recalled until approved),
    /// in one transaction: all of it or none. Returns the stored rows in the order given.
    /// </summary>
    Task<IReadOnlyList<PersonaMemoryAdminRow>> AddDraftedLoreAsync(long personaId, IReadOnlyList<string> texts, CancellationToken ct);

    /// <summary>
    /// Applies <paramref name="change"/> to one row in one transaction (a row lock, then one UPDATE): all of it or none. Approving a row whose
    /// source is not <see cref="MemorySources.Drafted"/> changes nothing and reports
    /// <see cref="PersonaMemoryUpdateStatus.NotDrafted"/>; approving stamps <c>approved_at</c> when not set yet.
    /// </summary>
    Task<PersonaMemoryUpdateResult> UpdateAsync(long personaId, long rowId, PersonaMemoryChange change, CancellationToken ct);

    Task<bool> DeleteAsync(long personaId, long rowId, CancellationToken ct);
}
