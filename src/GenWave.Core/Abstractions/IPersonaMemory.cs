using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F71.4-F71.6, STORY-194) — record/recall access to <c>station.persona_memory</c>: the
/// DJ's accrued and authored bits/callbacks a future prompt-assembly consumer blends into TTS patter
/// (Q4's persona work wires that consumer — this seam ships the contract and its store only, mirroring
/// <see cref="IPersonaStore"/>'s own original "seam before consumer" shape).
/// </summary>
public interface IPersonaMemory
{
    /// <summary>
    /// Records a new memory row for <paramref name="personaId"/>/<paramref name="kind"/>. When
    /// <paramref name="source"/> is <see cref="PersonaMemorySource.Accrued"/>, eviction of the oldest
    /// accrued rows beyond the configured per-(persona, kind) cap runs in the SAME transaction as this
    /// insert (SPEC F71.6); an <see cref="PersonaMemorySource.Authored"/> row never triggers — and is
    /// never subject to — that eviction. Returns the new row's id.
    /// </summary>
    Task<long> RecordAsync(long personaId, string kind, string content, PersonaMemorySource source, CancellationToken ct);

    /// <summary>
    /// Marks the memory row identified by <paramref name="id"/> as aired: increments
    /// <c>aired_count</c> and stamps <c>last_aired_at</c> to now. Callers MUST call this BEFORE
    /// dispatching the render that speaks it (SPEC F71.5) — marking first, not after, is what makes a
    /// crash between the mark and the render safe: the row already falls outside its recall window on
    /// the very next <see cref="RecallAsync"/> call, restart or not, so it is never offered twice.
    /// </summary>
    Task MarkAiredAsync(long id, CancellationToken ct);

    /// <summary>
    /// Recalls up to <paramref name="spec"/>'s <see cref="RecallSpec.Take"/> rows of
    /// <paramref name="personaId"/>'s memory matching <see cref="RecallSpec.Kind"/> and its recall
    /// window (SPEC F71.4) — see <see cref="RecallSpec"/>'s own remarks for the anti-repeat vs.
    /// callback shapes that one spec expresses. Ordered newest-first (never-aired rows first, then
    /// most-recently-aired), matching the <c>persona_memory_recall</c> index.
    /// </summary>
    Task<IReadOnlyList<PersonaMemoryEntry>> RecallAsync(long personaId, RecallSpec spec, CancellationToken ct);

    /// <summary>
    /// Returns every memory row for <paramref name="personaId"/> in <paramref name="source"/> — the
    /// card-export endpoint's read (SPEC F79.1, STORY-208). Unlike <see cref="RecallAsync"/>, this is
    /// not scoped to any one <c>kind</c> or aired-recency window: export wants every authored bit,
    /// callback, and note a persona carries, not a prompt-assembly-sized slice of one kind.
    /// <paramref name="source"/> is REQUIRED — there is no "every source" overload — so the only
    /// <c>persona_memory</c> read the export path can reach is filtered at the SQL layer; it can never
    /// regress into reading everything and trimming accrued rows out afterward in application code.
    /// </summary>
    Task<IReadOnlyList<PersonaMemoryEntry>> ListAsync(long personaId, PersonaMemorySource source, CancellationToken ct);
}
