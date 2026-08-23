using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The endpoint-facing write/read seam onto <c>station.announcement</c> (SPEC F143.1/.4/.5, STORY-357,
/// PLAN T339) — the <see cref="ILiquidsoapControl"/> placement precedent applied here: a Core-level
/// port a MediaLibrary repository implements directly, never widening the published
/// <c>GenWave.Abstractions</c> surface (that project's own remarks name
/// <c>ILiquidsoapControl</c>/<c>IShowStore</c>-shaped leaky contracts as staying internal by design).
///
/// <b>Deliberately narrow — exactly what <c>AnnouncementsController</c> (T339/T344) needs.</b>
/// <see cref="IAnnouncementSource"/> already owns the DIFFERENT, vend-side claim seam (PLAN T338/T341);
/// this port never grows a claim/vend member. Lifecycle transitions (mark-aired, decline, expire,
/// re-arm) landed on their OWN seam instead (<see cref="IAnnouncementLifecycle"/>, PLAN T343's
/// guardians) rather than growing this one — see that interface's own remarks for why.
/// <see cref="HistoryAsync"/> (PLAN T344) is the one exception to "narrow": the admin page's own
/// read (SPEC F146.2) is a real Host call site, so it lands here rather than waiting for a
/// hypothetical later need — see <c>GenWave.MediaLibrary.Station.AnnouncementRepository</c>'s own
/// remarks for the full store this seam exposes only a slice of.
/// </summary>
public interface IAnnouncementStore
{
    /// <summary>
    /// Accepts a new announcement, or folds it into an already-pending, case-folded-identical row
    /// (SPEC F143.5) — the endpoint never inspects which happened, only the id either way. Returns
    /// <see langword="null"/> when the store's own 280-char CHECK constraint declines the write (SPEC
    /// F143.4's DB backstop) — reachable only if a caller lets a longer message through than the
    /// endpoint's own <c>AnnouncementsOptions.MessageMaxChars</c> validation should have caught;
    /// treated as a 400 by the caller, never a raw 500. <paramref name="ttl"/> null means "use the
    /// store's own default" (SPEC F143.1, 900s) — the endpoint passes a value only when the caller
    /// supplied one and it already passed the 60–3600s bounds check.
    /// </summary>
    Task<long?> InsertOrCollapseAsync(
        string message, bool verbatim, string? requestedVoice, AnnouncementSubmitter submitter, TimeSpan? ttl, CancellationToken ct);

    /// <summary>
    /// Counts rows currently <c>state = 'pending'</c> — the station-wide pending-depth cap (SPEC
    /// F143.4, <c>AnnouncementsOptions.PendingDepthCap</c>) reads this before every insert; at cap, the
    /// endpoint refuses 429 with nothing written (never an eviction — contrast <c>IRequestStore</c>'s
    /// own evict-oldest shape for a best-effort listener wish; an announcement is a deliberate owner
    /// message).
    /// </summary>
    Task<int> CountPendingAsync(CancellationToken ct);

    /// <summary>
    /// Newest-first rows across every state, capped at <paramref name="limit"/> (SPEC F146.2,
    /// STORY-361, PLAN T344) — the F143.2 visible-decline/visible-expiry surface's own read: nothing
    /// this store transitions is ever hidden from <c>GET /api/announcements</c>, only listed. This
    /// seam never bounds <paramref name="limit"/> itself — the endpoint owns capping it to its own
    /// 50-default/200-max policy before calling here, the same "the endpoint enforces the policy,
    /// this seam just answers the query" split <see cref="CountPendingAsync"/>'s own depth-cap
    /// caller already keeps.
    /// </summary>
    Task<IReadOnlyList<AnnouncementHistoryEntry>> HistoryAsync(int limit, CancellationToken ct);
}
