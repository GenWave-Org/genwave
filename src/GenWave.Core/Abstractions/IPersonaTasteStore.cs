using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F82.1, F84.1-F84.3; STORY-213; ARCHITECTURE.md "Personalities on air") — CRUD access to
/// <c>station.persona_taste</c>: a persona's authored/operator/accrued taste opinions. This seam ships
/// the contract and its store only — no consumer lands with it yet. The ranker (T63) reads via
/// <see cref="ListAsync"/>; card import (T66-T69) and the accrual thumb endpoint (T70) write via
/// <see cref="InsertAsync"/>/<see cref="ReplaceAsync"/>; F84.3's cap-50-weakest-evicted eviction and
/// F84's ±0.2 nudge math are T70's, layered on top of these primitives rather than shipped here.
/// </summary>
public interface IPersonaTasteStore
{
    /// <summary>
    /// Inserts a new row unconditionally — no identity check against existing rows. Returns the new
    /// row's id.
    /// </summary>
    Task<long> InsertAsync(long personaId, TasteRule rule, PersonaTasteSource source, CancellationToken ct);

    /// <summary>
    /// Upserts by identity: (<paramref name="personaId"/>, <paramref name="source"/>,
    /// <paramref name="rule"/>'s predicate and context) is the natural key for "the same opinion" — a
    /// second write against that identity updates the existing row's weight (and
    /// <c>updated_at</c>) rather than inserting a duplicate. This is the primitive card import's
    /// authored-row upsert and the accrual thumb's nudge (T70) both build on; the ±0.2 clamp and
    /// cap-50 eviction math themselves are T70's, not this method's. Returns the affected row's id
    /// (existing on update, new on insert).
    /// </summary>
    Task<long> ReplaceAsync(long personaId, TasteRule rule, PersonaTasteSource source, CancellationToken ct);

    /// <summary>
    /// Lists every row for <paramref name="personaId"/>, optionally narrowed to one
    /// <paramref name="source"/> (<see langword="null"/> = every source).
    /// </summary>
    Task<IReadOnlyList<PersonaTasteEntry>> ListAsync(long personaId, PersonaTasteSource? source, CancellationToken ct);

    /// <summary>
    /// Deletes every row for <paramref name="personaId"/> in <paramref name="source"/> — the bulk
    /// primitive card import uses to clear a persona's previously-imported authored rows before
    /// inserting the freshly imported set. Returns the number of rows deleted.
    /// </summary>
    Task<int> DeleteAsync(long personaId, PersonaTasteSource source, CancellationToken ct);
}
