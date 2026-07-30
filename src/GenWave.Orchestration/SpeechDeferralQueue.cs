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
    /// The full deferral behind <see cref="NextDue"/> (gh-#254): the earliest-due pending entry,
    /// ordered by <see cref="SpeechDeferral.Due"/> ascending with the SAME kind tiebreak
    /// <see cref="TryDequeueDue"/> contracts (declaration order — SignOff before SignOn), or
    /// <see langword="null"/> when nothing is pending. Read-only — never consumes. Exposed so
    /// boundary-fit selection can see WHAT is coming (kind + handoff context feed the patter
    /// estimates), not merely when; <see cref="NextDue"/> stays for callers that only need the
    /// instant.
    /// </summary>
    public SpeechDeferral? PeekNextDue()
    {
        lock (gate)
        {
            return pending.Count == 0
                ? null
                : pending.Values.OrderBy(deferral => deferral.Due).ThenBy(deferral => deferral.Kind).First();
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
    /// <param name="handoff">
    /// SPEC F92.1/F92.2 (STORY-243, PLAN T124) additive payload for
    /// <see cref="SpeechDeferralKind.SignOff"/>/<see cref="SpeechDeferralKind.SignOn"/> — see
    /// <see cref="HandoffContext"/>. <see langword="null"/> for every other kind.
    /// </param>
    public void Enqueue(
        SpeechDeferralKind kind, string reason, DateTimeOffset? due = null, HandoffContext? handoff = null)
    {
        var deferral = new SpeechDeferral(kind, due ?? timeProvider.GetUtcNow(), reason, handoff);
        lock (gate)
        {
            pending[kind] = deferral;
        }
    }

    /// <summary>
    /// Removes any pending deferral of <paramref name="kind"/>, regardless of its due time — the
    /// explicit "the boundary this was armed for is no longer coming" retraction (SPEC F92.1
    /// revisit, PLAN T124) that <see cref="Enqueue"/>'s own supersede-by-kind cannot express:
    /// supersede only ever REPLACES a pending entry of the same kind with a newer one, it has no way
    /// to say "nothing of this kind should be pending any more." A schedule write that moves a
    /// boundary OUT of the F74.3 lookahead window (or removes it) needs exactly that — the
    /// handoff-ceremony producer calls this once the current snapshot no longer names an in-window
    /// boundary, so a ceremony armed for the OLD boundary can never air stale. A no-op when nothing
    /// of that kind is pending.
    /// </summary>
    public void Clear(SpeechDeferralKind kind)
    {
        lock (gate)
        {
            pending.Remove(kind);
        }
    }

    /// <summary>
    /// Removes and returns every deferral due at or before <paramref name="now"/>, ordered by
    /// <see cref="SpeechDeferral.Due"/> ascending with <see cref="SpeechDeferralKind"/>'s own
    /// declaration order (<see cref="SpeechDeferralKind.SignOff"/> before
    /// <see cref="SpeechDeferralKind.SignOn"/>) as the tiebreak for two deferrals due at the exact
    /// same instant (T124 review finding F1) — this is this queue's own contract, load-bearing for
    /// any caller (a handoff ceremony) whose two pieces must always air in a fixed relative order
    /// regardless of which one happens to be due first. Deliberately NOT <c>pending.Values</c>'
    /// raw enumeration order: <see cref="Dictionary{TKey,TValue}"/> reuses a freed slot LIFO on the
    /// next insert, so after one drain-then-re-enqueue cycle its enumeration order silently stops
    /// matching insertion order — the exact bug this ordering closes (reproduced: sign-off/sign-on
    /// airing backwards from the second boundary onward). Call this only from a genuine boundary
    /// decision (SPEC F74.1) — the caller, not this queue, is what guarantees "never mid-track".
    /// </summary>
    public IReadOnlyList<SpeechDeferral> TryDequeueDue(DateTimeOffset now)
    {
        lock (gate)
        {
            if (pending.Count == 0) return [];

            var due = pending.Values
                .Where(deferral => deferral.Due <= now)
                .OrderBy(deferral => deferral.Due)
                .ThenBy(deferral => deferral.Kind)
                .ToList();
            foreach (var deferral in due) pending.Remove(deferral.Kind);
            return due;
        }
    }
}
