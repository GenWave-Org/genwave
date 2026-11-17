using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F218.43, F218.44, F218.46, STORY-550) — callback eligibility and the ask record over
/// <c>station.persona_memory</c> and <c>station.memory_offer</c>. Windows are durations measured by the
/// store's one clock (the database's), never by a caller-computed cutoff.
/// </summary>
public interface ICallbackStore
{
    /// <summary>
    /// Up to <paramref name="take"/> memories the persona may call back on: not forgotten, not an unapproved draft,
    /// kind lore/episode/life, not used within its rest (lore 6 h, episode and life 48 h) and not asked within 2 h.
    /// Least recently asked first (never asked first), then newest.
    /// </summary>
    Task<IReadOnlyList<RecalledMemory>> EligibleAsync(long personaId, int take, CancellationToken ct);

    /// <summary>Elapsed time since the persona's last used ask and last ask of any outcome.</summary>
    Task<CallbackOfferHistory> LastOfferAsync(long personaId, CancellationToken ct);

    /// <summary>
    /// Writes one ask (used = false) before the render and evicts asks older than 28 days, in one statement.
    /// Returns the offer id.
    /// </summary>
    Task<long> OfferAsync(long memoryId, long personaId, CancellationToken ct);

    /// <summary>
    /// Records the outcome in one transaction: the offer row gains its utterance, used flag and matched words and,
    /// when used, the memory's aired_count rises by one and last_aired_at is set. An already-used offer is left
    /// alone, so a retry cannot count twice.
    /// </summary>
    Task SettleAsync(long offerId, long? utteranceId, bool used, IReadOnlyList<string> matchedWords, CancellationToken ct);
}
