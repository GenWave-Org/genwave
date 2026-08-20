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
    ///
    /// <para>
    /// <b>Deliberately NOT gate-filtered (round-3 review finding, SPEC F124.1/F124.2), unlike
    /// <see cref="PeekNextDue"/>.</b> This member never reads a <see cref="SpeechDeferral.NotBefore"/>
    /// at all — a held (gated) entry's raw <see cref="SpeechDeferral.Due"/> counts here exactly the same
    /// as an ungated one's. That is safe only because this member is NOT on
    /// <see cref="Orchestrator.GetNextAsync"/>'s own planning path — it never decides whether a
    /// <c>BoundaryFitPlan</c> gets built, never feeds <see cref="Orchestrator.ShouldDeclineFinalUnit"/>,
    /// and no production caller consults it today (<c>ClockAnchoredImagingProducer</c>'s own doc
    /// mentions it only in passing; every actual reader is a test asserting queue state directly). The
    /// planner reads <see cref="PeekNextDue"/> exclusively for exactly the reason this member must NOT
    /// be repurposed for that role without first teaching it the same gate: a held SignOn's raw
    /// <see cref="SpeechDeferral.Due"/> is stale-but-not-yet-airable, and reporting it here as "the next
    /// due instant" would silently reintroduce the round-2 blind-peek defect the moment some future
    /// caller wired this property into a planning decision instead of <see cref="PeekNextDue"/>.
    /// </para>
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
    /// The full deferral behind <see cref="NextDue"/> (gh-#254): the earliest-due, UN-GATED pending
    /// entry, ordered by <see cref="SpeechDeferral.Due"/> ascending with the SAME kind/discriminator
    /// tiebreak <see cref="TryDequeueDue"/> contracts (declaration order — SignOff before SignOn —
    /// then discriminator, ordinal), or <see langword="null"/> when nothing UN-GATED is pending.
    /// Read-only — never consumes.
    ///
    /// <para>
    /// <b>Skips a held (<see cref="SpeechDeferral.NotBefore"/>-gated) entry entirely (SPEC
    /// F124.1/F124.2, PLAN T267, round-2 review findings F1/F2 — the "gate at the drain, blind at the
    /// peek" defect).</b> Checked against REAL wall-clock time, the SAME <see cref="TimeProvider"/>
    /// reading <see cref="TryDequeueDue"/> gates against — never the caller's own due-time arithmetic —
    /// so a held entry is exactly as invisible to the PLANNER (<c>Orchestrator.GetNextAsync</c>'s own
    /// peek, which decides whether a boundary fit exists at all) as it already was to the DRAIN. Before
    /// this fix, a peek blind to the gate kept reporting a held-but-not-yet-airable SignOn as "next up"
    /// for as long as its hold lasted: <c>Orchestrator.GetNextAsync</c> kept building a fresh
    /// <c>BoundaryFitPlan</c> for it, <c>ShouldDeclineFinalUnit</c> kept declining (the SAME below-floor
    /// numbers every repeat), and — with the default cadence (<c>BackAnnounceAfterEachTrack</c>) on —
    /// <c>TryServeCeremonyOnlyUnitAsync</c> kept rendering a FRESH back-announce every single pull,
    /// since that was the one piece its own drain could still Kick while the SignOn itself stayed
    /// blocked by <see cref="TryDequeueDue"/>'s own (already-correct) gate check — a repeated-back-
    /// announce loop strictly worse than the incident this whole seam exists to fix. Once <c>now</c>
    /// itself passed the held entry's stale <see cref="SpeechDeferral.Due"/>, the SAME blindness flipped
    /// to the opposite failure: <c>untilDue</c> went negative, <c>GetNextAsync</c>'s own <c>untilDue &gt;
    /// TimeSpan.Zero</c> guard refused to build ANY fit at all, for ANY kind — not just the held one —
    /// for the remainder of the hold, since the held entry kept winning the earliest-due comparison
    /// against every other, perfectly eligible pending deferral. Skipping a gated entry here restores
    /// both: the loop stops (nothing left to re-decline once the held entry is invisible), and whichever
    /// UN-GATED deferral is next in line (or none) heads the fit exactly as if the held entry were not
    /// pending at all — F74.3 bias, a gh-#300 decline, a fresh boundary's own SignOff, all stay live
    /// throughout the hold.
    /// </para>
    ///
    /// <para>
    /// <b>Interaction with sort order and <see cref="EnqueueIfAbsent"/>.</b> The gate is applied as a
    /// pre-filter, BEFORE <see cref="InDrainOrder"/>'s own Due/Kind/Discriminator ordering runs — never
    /// a post-hoc skip over an already-sorted sequence — so a held entry never "uses up" or shifts the
    /// tiebreak among the entries that remain: the SAME stable order <see cref="TryDequeueDue"/> would
    /// eventually drain them in is exactly what this method reports among whatever is left after gated
    /// entries are removed. <see cref="EnqueueIfAbsent"/> is unaffected — it never reads a <c>NotBefore</c>
    /// at all, so a producer re-arming the identical due instant on every tick (its own no-op re-arm
    /// contract, unrelated to this gate) behaves exactly as it always has, held entry or not.
    /// </para>
    /// </summary>
    public SpeechDeferral? PeekNextDue()
    {
        lock (gate)
        {
            if (pending.Count == 0) return null;

            var realNow = timeProvider.GetUtcNow();
            return InDrainOrder(pending.Values.Where(deferral =>
                    deferral.NotBefore is not { } notBefore || notBefore <= realNow))
                .FirstOrDefault();
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
    /// Additive (SPEC F107.3, STORY-297, PLAN T224; reshaped F125.2/F125.3) — see
    /// <see cref="SpeechDeferral.Context"/>. The T226 Host ticker is this parameter's one production
    /// caller, passing the very <see cref="ContextSegmentFacts"/> <c>ContextPipeline.TickAsync</c> just
    /// handed it for the due provider. Every pre-F107 kind leaves this <see langword="null"/>,
    /// unchanged.
    /// </param>
    /// <param name="notBefore">
    /// Additive (SPEC F124.1/F124.2, PLAN T267, round-1 review findings F1/F2) — see
    /// <see cref="SpeechDeferral.NotBefore"/>. <see langword="null"/> (the default) for every caller
    /// except <c>Orchestrator.HoldSignOnPastQueuedTail</c>.
    /// </param>
    public void Enqueue(
        SpeechDeferralKind kind,
        string reason,
        DateTimeOffset? due = null,
        HandoffContext? handoff = null,
        string? discriminator = null,
        ContextSegmentFacts? context = null,
        DateTimeOffset? notBefore = null)
    {
        var deferral = new SpeechDeferral(
            kind, due ?? timeProvider.GetUtcNow(), reason, handoff, discriminator, context, notBefore);
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
    /// Like <see cref="Clear"/>, but leaves a HELD entry — one whose <see cref="SpeechDeferral.NotBefore"/>
    /// gate has not yet opened against REAL wall-clock time — exactly where it is (SPEC F124.1/F124.2,
    /// PLAN T267, round-1 review finding F2). A pending deferral still waiting on its own not-before
    /// floor is LIVE, not stale, even when whatever prompted this call has concluded that nothing of
    /// <paramref name="kind"/> belongs here any more: it is waiting on the tail it was deliberately
    /// queued behind to finish draining, a fact this call's own reason for clearing (a schedule
    /// re-evaluation, typically) knows nothing about. <c>Orchestrator</c>'s own <c>ClearCeremony</c>
    /// local — the handoff-ceremony producer's window-exit/gap-to-gap/self-handoff retraction — is the
    /// one caller; see that method's own remarks for why silently destroying a held-but-not-yet-airable
    /// SignOn there was the F2 defect this closes. An entry with no <see cref="SpeechDeferral.NotBefore"/>
    /// at all (every kind except a held SignOn) is unaffected — always stale by this definition, exactly
    /// what <see cref="Clear"/> already removed.
    /// </summary>
    public void ClearStale(SpeechDeferralKind kind)
    {
        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            var keys = pending
                .Where(entry => entry.Key.Kind == kind
                    && (entry.Value.NotBefore is not { } notBefore || notBefore <= now))
                .Select(entry => entry.Key)
                .ToList();
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
    /// <param name="queuedAhead">
    /// SPEC F124.4 (PLAN T269) — the feeder's own already-queued-runtime estimate for this pass,
    /// forwarded verbatim from <c>Orchestrator</c> (mirrors <c>BuildBoundaryFit</c>'s identical
    /// "feeder measurement, unknown = zero" coalesce — the caller, never this queue, resolves a null
    /// <c>PlayoutContext.QueuedAheadMs</c> to zero before it ever reaches here). Consulted ONLY by the
    /// <see cref="SpeechDeferralKind.TimeDate"/> elapsed-due expiry check below; every other kind
    /// ignores it entirely. Defaults to <see langword="default"/> (zero) — every pre-T269 caller's
    /// behavior, unaffected, since <paramref name="timeDateStaleBudget"/> being <see langword="null"/>
    /// already skips the expiry check outright regardless of this value.
    /// </param>
    /// <param name="timeDateStaleBudget">
    /// SPEC F124.4 (PLAN T269) — the live elapsed-due expiry budget: a <see cref="SpeechDeferralKind.TimeDate"/>
    /// deferral draining more than this far past its own air-time (see the expiry check's own remarks
    /// below for the exact formula) is dropped undrained rather than airing an hour that has already
    /// passed. <see langword="null"/> (the default) disables the check entirely — every pre-T269
    /// caller's behavior, byte-identical, since nothing before T269 ever passed this parameter.
    /// <c>Orchestrator</c> reads the live <c>Station:Imaging:TimeAnnouncementBudgetSeconds</c> setting
    /// (SPEC F141.1 — widened from the original F124.4 shipped budget) fresh once per unit and
    /// forwards the resulting value here, so a live edit governs the very next drain with no process
    /// restart — the caller's job, not this queue's, exactly like every other live-editable knob this
    /// project threads through (SPEC F44.2's own precedent).
    /// </param>
    /// <param name="onExpired">
    /// SPEC F124.4 (PLAN T269) — invoked once per <see cref="SpeechDeferralKind.TimeDate"/> deferral
    /// the expiry check drops, AFTER this method's own lock has been released (never from inside the
    /// lock — a caller's callback, e.g. a logger call, must never run while this queue is held), with
    /// the dropped deferral itself (<see cref="SpeechDeferral.Due"/> names the armed hour — the SAME
    /// instant a successful drain would have spoken, see <c>Orchestrator.BuildTimeDateRequest</c>'s
    /// own remarks) and the air-time lateness already computed below. <see langword="null"/> (the
    /// default) is a legal "nobody needs to know" — the drop still happens either way; this is
    /// notification only, never a veto. <c>Orchestrator</c> is this parameter's one production caller,
    /// logging the SPEC F124.4 WARN from it.
    /// </param>
    /// <remarks>
    /// <b><see cref="SpeechDeferral.NotBefore"/> gates against REAL wall-clock time, never
    /// <paramref name="now"/></b> (SPEC F124.1/F124.2, PLAN T267, round-1 review finding F1). A caller
    /// may legitimately pass a <paramref name="now"/> far ahead of the real clock — the ceremony-only
    /// and straddle drains both do, deliberately, to reach a piece whose <see cref="SpeechDeferral.Due"/>
    /// has not technically arrived yet but effectively has once queued audio is accounted for — and
    /// <see cref="SpeechDeferral.Due"/> is right to honor that forced instant: it names the boundary a
    /// deferral belongs to, and a forced-forward "as of" reasonably advances past it.
    /// <see cref="SpeechDeferral.NotBefore"/> means something categorically different — "this may not
    /// leave the queue before wall-clock time genuinely reaches this instant" — so it is checked against
    /// this queue's own <see cref="TimeProvider"/> reading, taken fresh on every call, regardless of what
    /// <paramref name="now"/> the caller supplied. Without this split, the SAME forced-forward instant
    /// that correctly satisfies a held SignOff's <c>Due</c> would just as incorrectly satisfy its paired
    /// SignOn's re-armed eligibility the very next time that SAME SignOn became the peeked fit — the
    /// hold lasting exactly zero seconds, the round-1 defect this closes.
    ///
    /// <para>
    /// <b><see cref="SpeechDeferralKind.TimeDate"/> elapsed-due expiry (SPEC F124.4, PLAN T269).</b>
    /// Built beside this SAME not-before check, and follows the identical rule — compared against this
    /// queue's real clock reading, never <paramref name="now"/>, so a forced-future <paramref name="now"/>
    /// (the ceremony/straddle drains above) never makes a perfectly punctual TimeDate read as late
    /// merely because the CALLER pretended more time had passed than the wall clock agrees has.
    ///
    /// <b>Round-2 review finding F8 — the lateness arithmetic itself:</b> air-time lateness is
    /// <c>realNow + queuedAhead − Due</c>, NOT the naive <c>realNow − Due</c> — a predicate that
    /// compares only wall-clock "now" against <c>Due</c>, with no <c>queuedAhead</c> term, under-counts
    /// lateness by exactly the queued tail still ahead of this pass. The SAME reasoning
    /// <see cref="Orchestrator.HoldSignOnPastQueuedTail"/>'s own <c>NotBefore</c> arithmetic already
    /// applies to a held SignOn (a piece has not truly "aired late" merely because wall-clock passed its
    /// <c>Due</c> — it airs when the ALREADY-QUEUED audio ahead of it finishes, not one instant sooner)
    /// carries over here: a bare "has real time passed <c>Due</c>" would flag a TimeDate deferral stale
    /// well before it has actually gone stale on air, by exactly however much runtime is still queued
    /// ahead of the pull that would otherwise drain it.
    ///
    /// <b>Kind-scoped to TimeDate alone (F124.4's own "idents are deliberately exempt" ruling) — every
    /// other kind ignores <paramref name="timeDateStaleBudget"/>/<paramref name="queuedAhead"/> entirely,
    /// regardless of how late it drains.</b> A late ident is fine; a late time check invents the hour
    /// (the F71.8 never-invent-the-time class this whole seam exists to close).
    ///
    /// <b>No NotBefore/expiry cross-product to resolve.</b> A <see cref="SpeechDeferralKind.TimeDate"/>
    /// deferral never carries a <see cref="SpeechDeferral.NotBefore"/> — <see cref="Orchestrator.HoldSignOnPastQueuedTail"/>
    /// is that field's one production caller, and it only ever re-arms a <see cref="SpeechDeferralKind.SignOn"/>.
    /// So a TimeDate can never be simultaneously held and stale; the expiry check below does not need to
    /// (and does not) handle that combination — belt-and-suspenders anyway, <paramref name="hold"/> is
    /// applied in the FIRST pass below, before expiry is ever classified for anything, so a held kind is
    /// never even a candidate the expiry check looks at.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SpeechDeferral> TryDequeueDue(
        DateTimeOffset now,
        IReadOnlySet<SpeechDeferralKind>? hold = null,
        TimeSpan queuedAhead = default,
        TimeSpan? timeDateStaleBudget = null,
        Action<SpeechDeferral, TimeSpan>? onExpired = null)
    {
        IReadOnlyList<SpeechDeferral> due;

        // Dropped-for-staleness entries are collected here (inside the lock, where the expiry
        // decision is made) and reported to onExpired AFTER the lock is released below — a caller's
        // callback must never run while this queue is held.
        List<(SpeechDeferral Deferral, TimeSpan Lateness)>? expired = null;

        lock (gate)
        {
            if (pending.Count == 0) return [];

            var realNow = timeProvider.GetUtcNow();

            // Pass 1 (a QUERY, no side effects) — every candidate this drain would otherwise take:
            // due, NotBefore-satisfied, and NOT held. hold is applied HERE, before expiry is ever
            // classified for anything (see this method's own remarks) — a held kind can never reach
            // the expiry pass below. Materialized eagerly (ToList) rather than left lazy specifically
            // so pass 2's classification below runs exactly once per candidate, ever, regardless of
            // how this list is later consumed.
            var candidates = pending.Values.Where(deferral =>
                    deferral.Due <= now
                    && (deferral.NotBefore is not { } notBefore || notBefore <= realNow)
                    && (hold is null || !hold.Contains(deferral.Kind)))
                .ToList();

            // Pass 2 (a COMMAND, explicitly) — classifies each candidate as expired (SPEC F124.4,
            // TimeDate-only) or genuinely due, via a plain foreach rather than a LINQ predicate: a
            // side-effecting predicate embedded in a Where (the round-3 review's own CQS finding)
            // is a landmine the moment anything upstream enumerates the same query twice — e.g. a
            // future .Any() short-circuit before this method's own .ToList() — which would silently
            // double-classify (and double-WARN) the same drop. This loop touches each candidate once.
            var eligible = new List<SpeechDeferral>(candidates.Count);
            foreach (var deferral in candidates)
            {
                if (deferral.Kind == SpeechDeferralKind.TimeDate && timeDateStaleBudget is { } budget)
                {
                    var lateness = AirTimeLateness(realNow, queuedAhead, deferral.Due);
                    if (lateness > budget)
                    {
                        (expired ??= []).Add((deferral, lateness));
                        continue;
                    }
                }

                eligible.Add(deferral);
            }

            due = InDrainOrder(eligible).ToList();

            foreach (var deferral in due) pending.Remove((deferral.Kind, deferral.Discriminator));
            if (expired is not null)
                foreach (var (deferral, _) in expired) pending.Remove((deferral.Kind, deferral.Discriminator));
        }

        if (expired is not null)
            foreach (var (deferral, lateness) in expired)
                onExpired?.Invoke(deferral, lateness);

        return due;
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

    /// <summary>
    /// SPEC F124.4/F141.2 (PLAN T269/T326, review advisory) — the ONE air-time-lateness formula both
    /// this queue's own expiry classification (pass 2 above, round-2 review finding F8's own remarks)
    /// and <c>Orchestrator</c>'s post-drain F141.2 honesty classification share, rather than each
    /// independently retyping <c>now + queuedAhead - due</c> (a connascence-of-algorithm smell flagged
    /// at PLAN T326 review). <c>internal</c>, not <see langword="private"/>: <c>Orchestrator</c> lives
    /// in this SAME assembly and is this method's one caller outside this class.
    ///
    /// <para>
    /// <b>Not threaded through <see cref="TryDequeueDue"/>'s own <c>onExpired</c> callback (or a new
    /// "onSurvived" twin) instead</b> — the fuller extraction the review also floated, so
    /// <c>Orchestrator</c> never recomputes anything at all, reading the queue's own already-computed
    /// value back verbatim. That shape needs a second callback parameter (with this file's own
    /// paragraph-per-parameter documentation discipline) PLUS a caller-side correlation step in
    /// <c>Orchestrator</c>'s kind-switch loop (a <c>Dictionary&lt;SpeechDeferral, TimeSpan&gt;</c>
    /// populated before that loop even starts, since the callback fires inside this method, before
    /// <see cref="TryDequeueDue"/> ever returns) to hand the right value to the right deferral once the
    /// switch reaches its <c>TimeDate</c> arm — real plumbing, for a connascence-of-algorithm cleanup
    /// this one-line shared formula already resolves at a fraction of the diff. Two independent reads
    /// of the SAME <see cref="TimeProvider"/>, microseconds apart at most, is not a correctness gap a
    /// 90-second honesty threshold can even observe.
    /// </para>
    /// </summary>
    internal static TimeSpan AirTimeLateness(DateTimeOffset now, TimeSpan queuedAhead, DateTimeOffset due) =>
        now + queuedAhead - due;
}
