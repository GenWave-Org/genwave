namespace GenWave.Orchestration;

/// <summary>
/// The seam SPEC F74.1/F74.2/F74.4 (STORY-197) is built on: decouples "an ident is DUE" (a
/// wall-clock or unit-count trigger firing) from "an ident AIRS" (the next track-boundary
/// decision). A producer calls <see cref="Enqueue"/> the moment its trigger fires; a consumer at
/// a genuine boundary decision — <see cref="Orchestrator"/> plans a whole unit atomically before
/// the next track ever reaches air, so its call to <see cref="TryDequeueDue"/> happens at a
/// boundary by construction, never mid-track — drains whatever is due.
///
/// <para>
/// <b>Supersede-by-kind (F74.2):</b> at most one pending deferral per <see cref="SpeechDeferralKind"/>.
/// A newer <see cref="Enqueue"/> of the same kind overwrites the pending one before it is ever
/// drained; the superseded deferral is discarded and never airs.
/// </para>
///
/// <para>
/// <b>In-memory only (F74.4):</b> nothing here is persisted. A host restart drops every pending
/// deferral along with the rest of the process, and a fresh <see cref="SpeechDeferralQueue"/>
/// starts empty — there is no stale entry left to double-air. Regeneration relies on each
/// producer's own state being naturally rebuilt from schedule state (e.g. <see cref="Orchestrator"/>'s
/// unit counter restarting at zero, SPEC F42.1) rather than a durable deferral log.
/// </para>
///
/// <para>
/// Thread-safe: <see cref="Enqueue"/>, <see cref="TryDequeueDue"/>, and <see cref="NextDue"/> may
/// all be called concurrently — the Orchestrator's own boundary decision today, and any future
/// producer running on its own trigger (timer, admin action, etc.) tomorrow.
/// </para>
/// </summary>
/// <param name="timeProvider">
/// The clock <see cref="Enqueue"/> reads for its default <c>due</c> (immediate) and <see cref="NextDue"/>
/// reports against. Injected rather than <see cref="DateTimeOffset.UtcNow"/> so a fake clock can
/// drive deterministic boundary/wall-clock-slip tests.
/// </param>
public sealed class SpeechDeferralQueue(TimeProvider timeProvider)
{
    readonly object gate = new();
    readonly Dictionary<SpeechDeferralKind, SpeechDeferral> pending = new();

    /// <summary>
    /// The earliest <see cref="SpeechDeferral.Due"/> among all currently pending deferrals, or
    /// <see langword="null"/> when nothing is pending. Read-only — never consumes. Exposed for
    /// boundary-aware track selection (SPEC F74.3, PLAN T43) to bias toward tracks ending near
    /// this instant.
    /// </summary>
    public DateTimeOffset? NextDue
    {
        get
        {
            lock (gate)
            {
                return pending.Count == 0 ? null : pending.Values.Min(deferral => deferral.Due);
            }
        }
    }

    /// <summary>
    /// Enqueues a deferral of <paramref name="kind"/>, due at <paramref name="due"/> (defaults to
    /// now — "due immediately, air at the very next boundary"). A pending deferral of the same
    /// <paramref name="kind"/> is replaced (SPEC F74.2): the superseded one is discarded and never
    /// airs.
    /// </summary>
    /// <param name="kind">Which scheduled speech this is.</param>
    /// <param name="reason">A short, human-readable note carried for logs/diagnostics.</param>
    /// <param name="due">
    /// The instant this deferral becomes eligible to air; <see langword="null"/> means "now".
    /// </param>
    public void Enqueue(SpeechDeferralKind kind, string reason, DateTimeOffset? due = null)
    {
        var deferral = new SpeechDeferral(kind, due ?? timeProvider.GetUtcNow(), reason);
        lock (gate)
        {
            pending[kind] = deferral;
        }
    }

    /// <summary>
    /// Removes and returns every deferral due at or before <paramref name="now"/>. Call this only
    /// from a genuine boundary decision (SPEC F74.1) — the caller, not this queue, is what
    /// guarantees "never mid-track".
    /// </summary>
    public IReadOnlyList<SpeechDeferral> TryDequeueDue(DateTimeOffset now)
    {
        lock (gate)
        {
            if (pending.Count == 0) return [];

            var due = pending.Values.Where(deferral => deferral.Due <= now).ToList();
            foreach (var deferral in due) pending.Remove(deferral.Kind);
            return due;
        }
    }
}
