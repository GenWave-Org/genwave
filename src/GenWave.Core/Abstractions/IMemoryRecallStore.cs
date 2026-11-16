using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F218.19, F218.22, STORY-539) — the read side of <c>station.persona_memory</c> that recall
/// needs: eligible rows per kind, and the recently forgotten claims. Windows are durations, measured
/// by the store's one clock (the database's), never by a caller-computed cutoff.
/// </summary>
public interface IMemoryRecallStore
{
    /// <summary>
    /// Up to <see cref="MemoryCandidateQuery.Take"/> eligible rows of the persona's kind: not forgotten,
    /// not an unapproved draft, lore only when authored or approved, and not aired within the window.
    /// Ranked by <see cref="MemoryCandidateQuery.Order"/>.
    /// </summary>
    Task<IReadOnlyList<RecalledMemory>> CandidatesAsync(long personaId, MemoryCandidateQuery query, CancellationToken ct);

    /// <summary>Claim texts of any row forgotten within <paramref name="within"/>, newest forgotten first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<string>> RecentlyForgottenAsync(long personaId, TimeSpan within, int take, CancellationToken ct);

    /// <summary>
    /// Marks every row aired in one statement (aired_count + 1, last_aired_at = the store's now()), so
    /// the marks are atomic and cost one round-trip. Unknown ids are ignored.
    /// </summary>
    Task MarkAiredAsync(IReadOnlyList<long> ids, CancellationToken ct);
}
