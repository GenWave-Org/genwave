namespace GenWave.Core.Abstractions;

/// <summary>
/// Write/read seam for <c>station.request</c> (SPEC F87, STORY-224, PLAN T86). Deliberately narrow:
/// this ships only what T87's intake endpoint needs — insert (with the wish-retention sweep) plus
/// the station-wide pending-cap eviction path. The parser (T88), matcher (T89), and fulfillment
/// (T90) rungs each extend this seam with their own members in their own tasks rather than
/// speculatively landing them here now.
/// </summary>
public interface IRequestStore
{
    /// <summary>
    /// Inserts one pending wish, stamped <c>received_at = now()</c> and
    /// <c>expires_at = </c><paramref name="expiresAt"/>, returning the new row's id.
    /// <paramref name="wish"/> is the listener's raw text; never voiced, quoted, or echoed
    /// downstream (SPEC F87.7) — this seam only ever stores it, and only briefly.
    ///
    /// <para>
    /// Also runs the insert-time wish-retention sweep (SPEC F87.8) in the SAME
    /// statement/transaction as the insert — the same "eviction runs inside the write's own
    /// transaction, in application code, not a separate job or trigger" discipline
    /// <c>station.booth_log</c>'s own retention sweep already established. Every row whose
    /// <c>received_at</c> is older than the configured retention window has its <c>wish</c> column
    /// nulled; parsed predicates (<c>artist</c>/<c>title</c>/<c>moods</c>) and the row's outcome
    /// (<c>status</c>/<c>matched_media_id</c>/<c>fulfilled_at</c>) are never touched by the sweep and
    /// stay indefinitely.
    /// </para>
    /// </summary>
    Task<long> InsertAsync(string wish, DateTimeOffset expiresAt, CancellationToken ct);

    /// <summary>
    /// Counts rows currently <c>status = 'pending'</c> — the station-wide pending cap (SPEC F87.3,
    /// <c>Requests:PendingCap</c>) reads this before every insert to decide whether an eviction is
    /// needed first.
    /// </summary>
    Task<int> CountPendingAsync(CancellationToken ct);

    /// <summary>
    /// Evicts the single oldest pending row (lowest <c>received_at</c>) — the station-wide
    /// pending-cap eviction (SPEC F87.3): a POST that would push the pending count over
    /// <c>Requests:PendingCap</c> evicts the oldest pending request to make room rather than being
    /// rejected. A no-op when no pending row exists.
    /// </summary>
    Task EvictOldestPendingAsync(CancellationToken ct);
}
