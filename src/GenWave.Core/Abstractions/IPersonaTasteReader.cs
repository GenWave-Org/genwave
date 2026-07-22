using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Read-only narrowing of <see cref="IPersonaTasteStore"/> (SPEC F84.2 structural guarantee;
/// STORY-213) — the only <c>persona_taste</c> surface <c>GenWave.Orchestration.PersonaRanker</c>
/// (PLAN T63) depends on. F84.2 requires the ranker have "no code path" that writes
/// <c>persona_taste</c>; depending on this interface instead of the full
/// <see cref="IPersonaTasteStore"/> makes that guarantee structural rather than a code-review
/// promise — <see cref="IPersonaTasteStore.InsertAsync"/>/<see cref="IPersonaTasteStore.ReplaceAsync"/>/
/// <see cref="IPersonaTasteStore.DeleteAsync"/> are simply not visible on the seam the ranker holds, so
/// a future edit reaching for them is a compile error, not a review finding.
/// </summary>
public interface IPersonaTasteReader
{
    /// <inheritdoc cref="IPersonaTasteStore.ListAsync"/>
    Task<IReadOnlyList<PersonaTasteEntry>> ListAsync(long personaId, PersonaTasteSource? source, CancellationToken ct);
}
