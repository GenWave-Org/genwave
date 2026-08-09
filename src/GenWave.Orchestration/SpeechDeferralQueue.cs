namespace GenWave.Orchestration;

using GenWave.Core.Domain;

/// <summary>
/// The seam SPEC F74.1/F74.2/F74.4 (STORY-197) is built on: decouples "an ident is DUE" (a
/// wall-clock or unit-count trigger firing) from "an ident AIRS" (the next track-boundary
/// decision). A producer calls <see cref="Enqueue"/> the moment its trigger fires; a consumer at
/// a genuine boundary decision — <see cref="Orchestrator"/> plans a whole unit atomically before
/// the next track ever reaches air, so its call to <see cref="TryDequeueDue"/> happens at a
/// boundary by construction, never mid-track — drains whatever is due.
///
/// <para>
/// <b>Supersede-by-(kind, discriminator) (F74.2, F107.4):</b> at most one pending deferral per
/// <c>(<see cref="SpeechDeferralKind"/>, discriminator)</c> pair. A newer <see cref="Enqueue"/> of
/// the same pair overwrites the pending one before it is ever drained; the superseded deferral is
/// discarded and never airs. <see cref="Enqueue"/>'s discriminator parameter defaults to
/// <see langword="null"/> — every kind that predates F107 (<see cref="SpeechDeferralKind.StationId"/>,
/// <see cref="SpeechDeferralKind.SignOff"/>, <see cref="SpeechDeferralKind.SignOn"/>) always enqueues
/// with a null discriminator, so its supersede stays exactly one-pending-per-kind, byte-identical to
/// pre-F107 behavior. Only <see cref="SpeechDeferralKind.Context"/> (STORY-297) enqueues with a
/// non-null discriminator (the originating provider's key) — so two different providers' deferrals
/// (e.g. weather, history) coexist as pending, while two enqueues from the SAME provider still
/// collapse to the newer one.
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
/// Thread-safe: <see cref="Enqueue"/>, <see cref="EnqueueIfAbsent"/>, <see cref="TryDequeueDue"/>, and
/// <see cref="NextDue"/> may all be called concurrently — the Orchestrator's own boundary decision
/// today, and any future producer running on its own trigger (timer, admin action, etc.) tomorrow.
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

    // SPEC F107.4: the supersede key is (Kind, Discriminator), not Kind alone. A value-tuple key
    // gives correct structural equality/hashing (including a null Discriminator) with no extra
    // wrapper type — every pre-F107 kind (StationId, SignOff, SignOn) always enqueues with a null
    // discriminator, so its slot in this dictionary is exactly the (Kind, null) it always was, byte-
    // identical in every observable way to the old Dictionary<SpeechDeferralKind, SpeechDeferral>.
    readonly Dictionary<(SpeechDeferralKind Kind, string? Discriminator), SpeechDeferral> pending = new();

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
    /// ordered by <see cref="SpeechDeferral.Due"/> ascending with the SAME kind/discriminator
    /// tiebreak <see cref="TryDequeueDue"/> contracts (declaration order — SignOff before SignOn —
    /// then discriminator, ordinal), or <see langword="null"/> when nothing is pending. Read-only —
    /// never consumes. Exposed so boundary-fit selection can see WHAT is coming (kind + handoff
    /// context feed the patter estimates), not merely when; <see cref="NextDue"/> stays for callers
    /// that only need the instant.
    /// </summary>
    public SpeechDeferral? PeekNextDue()
    {
        lock (gate)
        {
            return pending.Count == 0 ? null : InDrainOrder(pending.Values).First();
        }
    }

    /// <summary>
    /// The pending deferral occupying exactly the <paramref name="kind"/>/<paramref name="discriminator"/>
    /// slot, or <see langword="null"/> when nothing does — read-only, never consumes. Unlike
    /// <see cref="PeekNextDue"/> (the earliest-due entry across every slot), this looks up ONE named
    /// slot regardless of where — or whether — it sorts first in due order. SPEC F111.3 (PLAN T235):
    /// the straddle assembly reads a pending SignOn's current <see cref="SpeechDeferral.Handoff"/>
    /// back this way at plan time, before re-<see cref="Enqueue"/>-ing an enriched copy of it (the
    /// same supersede-by-key path <see cref="Enqueue"/>'s own remarks describe).
    /// </summary>
    public SpeechDeferral? Peek(SpeechDeferralKind kind, string? discriminator = null)
    {
        lock (gate)
        {
            return pending.GetValueOrDefault((kind, discriminator));
        }
    }

    /// <summary>
    /// Enqueues a deferral of <paramref name="kind"/>, due at <paramref name="due"/> (defaults to
    /// now — "due immediately, air at the very next boundary"). A pending deferral of the same
    /// <paramref name="kind"/>/<paramref name="discriminator"/> pair is replaced (SPEC F74.2,
    /// F107.4): the superseded one is discarded and never airs.
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
    /// <param name="discriminator">
    /// Additive (SPEC F107.4, STORY-297): the supersede sub-key alongside <paramref name="kind"/> —
    /// see <see cref="SpeechDeferral.Discriminator"/>. Defaults to <see langword="null"/>, which is
    /// what every pre-F107 caller passes (implicitly, by omission) — supersede for those kinds stays
    /// exactly one-pending-per-kind, byte-identical to pre-F107 behavior. Only
    /// <see cref="SpeechDeferralKind.Context"/> callers pass a non-null value (the provider key).
    /// </param>
    /// <param name="context">
    /// Additive (SPEC F107.3, STORY-297, PLAN T224) — see <see cref="SpeechDeferral.Context"/>. The
    /// T226 Host ticker is this parameter's one production caller, passing the very
    /// <see cref="ContextContent"/> <c>ContextPipeline.TickAsync</c> just handed it. Every pre-F107
    /// kind leaves this <see langword="null"/>, unchanged.
    /// </param>
    public void Enqueue(
        SpeechDeferralKind kind,
        string reason,
        DateTimeOffset? due = null,
        HandoffContext? handoff = null,
        string? discriminator = null,
        ContextContent? context = null)
    {
        var deferral = new SpeechDeferral(
            kind, due ?? timeProvider.GetUtcNow(), reason, handoff, discriminator, context);
        lock (gate)
        {
            pending[(kind, discriminator)] = deferral;
        }
    }

    /// <summary>
    /// Enqueues a deferral of <paramref name="kind"/> — but ONLY when nothing is already pending for
    /// the same <paramref name="kind"/>/<paramref name="discriminator"/> pair; a no-op, not a
    /// supersede, when one already occupies that slot (PLAN T230 review F1). This is the conditional
    /// twin of <see cref="Enqueue"/>'s own always-overwrite supersede (SPEC F74.2): a producer that
    /// RE-DERIVES the same due instant on every tick while waiting for it to arrive (e.g.
    /// <see cref="ClockAnchoredImagingProducer"/>'s top-of-hour recompute) must never race its own
    /// not-yet-drained deferral off the queue merely because the wall clock ticked past that due
    /// instant before a boundary drained it. That is the exact bug this method closes:
    /// <see cref="Enqueue"/>'s unconditional overwrite silently discarded a still-pending, due-but-
    /// unaired deferral the moment the SAME producer's next tick recomputed a LATER due instant for
    /// the FOLLOWING hour — the deferral for the hour that just turned never aired. A pending FUTURE
    /// deferral for the same slot is left exactly as untouched as a pending due-or-past one; for a
    /// producer that always recomputes the identical due instant while one is still pending, that is a
    /// functional no-op either way (SPEC F110.1/F110.3's own "a pending future deferral for the same
    /// target hour may re-arm freely" contract holds because re-arming it changes nothing observable).
    /// </summary>
    /// <param name="kind">Which scheduled speech this is.</param>
    /// <param name="reason">A short, human-readable note carried for logs/diagnostics.</param>
    /// <param name="due">
    /// The instant this deferral becomes eligible to air, when it IS enqueued; <see langword="null"/>
    /// means "now". Ignored when the slot is already occupied.
    /// </param>
    /// <param name="discriminator">
    /// The supersede sub-key alongside <paramref name="kind"/> — see <see cref="Enqueue"/>'s own
    /// remarks for the full contract. Defaults to <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the deferral was enqueued; <see langword="false"/> when a pending
    /// entry already occupied the <paramref name="kind"/>/<paramref name="discriminator"/> slot and was
    /// left untouched.
    /// </returns>
    public bool EnqueueIfAbsent(
        SpeechDeferralKind kind,
        string reason,
        DateTimeOffset? due = null,
        string? discriminator = null)
    {
        lock (gate)
        {
            var key = (kind, discriminator);
            if (pending.ContainsKey(key))
                return false;

            pending[key] = new SpeechDeferral(kind, due ?? timeProvider.GetUtcNow(), reason, Discriminator: discriminator);
            return true;
        }
    }

    /// <summary>
    /// Removes every pending deferral of <paramref name="kind"/>, across ALL discriminators,
    /// regardless of due time — the explicit "the boundary this was armed for is no longer coming"
    /// retraction (SPEC F92.1 revisit, PLAN T124) that <see cref="Enqueue"/>'s own supersede cannot
    /// express: supersede only ever REPLACES a pending entry of the same
    /// <paramref name="kind"/>/discriminator pair with a newer one, it has no way to say "nothing of
    /// this kind should be pending any more." A schedule write that moves a boundary OUT of the
    /// F74.3 lookahead window (or removes it) needs exactly that — the handoff-ceremony producer
    /// calls this once the current snapshot no longer names an in-window boundary, so a ceremony
    /// armed for the OLD boundary can never air stale. A no-op when nothing of that kind is pending.
    ///
    /// <para>
    /// Kind-wide, not <c>(kind, discriminator)</c>-scoped (SPEC F107.4 review): every existing caller
    /// (SignOff/SignOn) always enqueues with a null discriminator, so there is at most one entry to
    /// clear per kind today and this is byte-identical to the pre-F107 single-key removal. Kept
    /// kind-wide rather than adding a narrower overload because no caller has yet needed to retract
    /// just one provider's <see cref="SpeechDeferralKind.Context"/> deferral while leaving another's
    /// pending — a narrower <c>Clear(kind, discriminator)</c> overload can be added if/when one does.
    /// </para>
    /// </summary>
    public void Clear(SpeechDeferralKind kind)
    {
        lock (gate)
        {
            var keys = pending.Keys.Where(key => key.Kind == kind).ToList();
            foreach (var key in keys) pending.Remove(key);
        }
    }

    /// <summary>
    /// Removes and returns every deferral due at or before <paramref name="now"/>, ordered by
    /// <see cref="SpeechDeferral.Due"/> ascending with <see cref="SpeechDeferralKind"/>'s own
    /// declaration order (<see cref="SpeechDeferralKind.SignOff"/> before
    /// <see cref="SpeechDeferralKind.SignOn"/>) as the FIRST tiebreak for two deferrals due at the
    /// exact same instant (T124 review finding F1) — this is this queue's own contract, load-bearing
    /// for any caller (a handoff ceremony) whose two pieces must always air in a fixed relative order
    /// regardless of which one happens to be due first. <see cref="SpeechDeferral.Discriminator"/>
    /// (ordinal) is a SECOND tiebreak, added for F107.4: two same-kind, same-instant deferrals from
    /// different providers (e.g. weather and history both due at once) still drain in a stable,
    /// deterministic order — every pre-F107 kind's discriminator is always null, so this second
    /// tiebreak is a no-op tie among them and changes nothing about their relative order. Deliberately
    /// NOT <c>pending.Values</c>' raw enumeration order: <see cref="Dictionary{TKey,TValue}"/> reuses
    /// a freed slot LIFO on the next insert, so after one drain-then-re-enqueue cycle its enumeration
    /// order silently stops matching insertion order — the exact bug this ordering closes (reproduced:
    /// sign-off/sign-on airing backwards from the second boundary onward). Call this only from a
    /// genuine boundary decision (SPEC F74.1) — the caller, not this queue, is what guarantees "never
    /// mid-track".
    /// </summary>
    /// <param name="now">The instant to drain as of — every deferral due at or before this fires.</param>
    /// <param name="hold">
    /// SPEC F111.2 (PLAN T235 — the /plan-ruled parameter shape over queue-side state, the smallest
    /// sound one since this only ever needs to suppress a kind for exactly ONE call): kinds to leave
    /// pending even though due, for this call only. The straddle seam's own forced-forward drain
    /// (<c>Orchestrator.GetNextAsync</c>, called with <paramref name="now"/> advanced to a SignOff's
    /// own <see cref="SpeechDeferral.Due"/> rather than the real "now" — mirrors
    /// <c>TryServeCeremonyOnlyUnitAsync</c>'s identical "drain as of a future instant, not now"
    /// precedent) must never sweep the paired SignOn up in that same forced call, even though today's
    /// <c>SignOffLeadTime</c> gap already keeps their due times apart on its own — belt-and-suspenders,
    /// and self-documenting at the call site. Defaults to <see langword="null"/> (nothing held) — every
    /// pre-T235 caller's behavior, byte-identical: an entry a plain <c>deferral.Due &lt;= now</c>
    /// already excludes stays excluded regardless of whether this parameter also names its kind, and a
    /// held-but-due entry is left exactly where <see cref="Enqueue"/> put it, ready for a later call
    /// (with no hold, or a different one) to pick up.
    /// </param>
    public IReadOnlyList<SpeechDeferral> TryDequeueDue(
        DateTimeOffset now, IReadOnlySet<SpeechDeferralKind>? hold = null)
    {
        lock (gate)
        {
            if (pending.Count == 0) return [];

            var due = InDrainOrder(pending.Values.Where(deferral =>
                    deferral.Due <= now && (hold is null || !hold.Contains(deferral.Kind))))
                .ToList();
            foreach (var deferral in due) pending.Remove((deferral.Kind, deferral.Discriminator));
            return due;
        }
    }

    // Shared ordering for PeekNextDue/TryDequeueDue (T223 review, F3): both callers need the SAME
    // Due-ascending, (Kind, Discriminator)-tiebreak order — see TryDequeueDue's own remarks for why
    // this exact order is load-bearing rather than incidental. Extracted so the two can never drift
    // apart from each other silently; behavior is unchanged (still a stable OrderBy/ThenBy chain).
    static IEnumerable<SpeechDeferral> InDrainOrder(IEnumerable<SpeechDeferral> deferrals) =>
        deferrals
            .OrderBy(deferral => deferral.Due)
            .ThenBy(deferral => deferral.Kind)
            .ThenBy(deferral => deferral.Discriminator, StringComparer.Ordinal);
}
