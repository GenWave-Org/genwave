namespace GenWave.Orchestration;

using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// SPEC F190 (STORY-457, PLAN T532) — extracted from <see cref="Orchestrator"/>: arms, holds and
/// enriches the two-piece handoff ceremony (SignOff/SignOn) as a boundary enters the F74.3 lookahead
/// window, with its own dedupe matrix and warn-once state. A pure move — no behaviour change from the
/// pre-T532 <c>Orchestrator.EnqueueHandoffCeremonyAsync</c>/<c>CaptureCrossingTrackForHeldSignOn</c>/
/// <c>HoldSignOnPastQueuedTail</c> shape. <see cref="Orchestrator.SignOffLeadTime"/> stays put (SPEC
/// F190.4 — GenWave.Host's <c>BoundaryCadenceCovenantPostConfigure</c> reads it there); this producer
/// reads it from there rather than owning a second copy.
/// </summary>
public sealed class HandoffCeremonyProducer(
    SpeechDeferralQueue deferralQueue,
    IBoundaryBiasProvider boundaryBiasProvider,
    ILogger<HandoffCeremonyProducer> logger,
    CachingScheduleResolver? scheduleResolver = null,
    IPersonaStore? personaStore = null,
    ISpeakerSnapshotSource? speakerSnapshots = null)
{
    // SPEC F92.1/F92.3 arm-once state (T124 review finding F2), widened at F116.2/PLAN T248 to also
    // key on show id: the (BoundaryAt, outgoing persona id, incoming persona id, outgoing show id,
    // incoming show id) tuple ArmAsync last acted on — null before the first unit, or once a boundary
    // has left the window and was explicitly cleared. Re-evaluating this producer every unit is by
    // design (a schedule write must be noticed promptly), but ACTING on the SAME tuple twice is not:
    // see ArmAsync's own remarks for the double-sign-off bug this guards against. The two show-id
    // members are additive (T248): a showless schedule always reads both as null on both sides, so
    // this tuple behaves byte-identically to the pre-T248 triple for every station that has never
    // assigned a show — the widening only ever matters for an in-window edit that changes a block's
    // show_id without also changing its persona_id, which otherwise this arm-once guard would wrongly
    // treat as "nothing changed".
    (DateTimeOffset BoundaryAt, long? OutgoingPersonaId, long? IncomingPersonaId, long? OutgoingShowId, long? IncomingShowId)? lastArmedHandoff;

    // T124 review finding F7: fires at most once for the life of this producer — a null
    // scheduleResolver makes ArmAsync a permanent no-op, which would otherwise be completely silent
    // (no format-clock schedule wired is a perfectly valid, common station shape, but an operator who
    // DID intend to wire one deserves one loud signal that it never arrived).
    bool scheduleResolverMissingWarned;

    /// <summary>
    /// SPEC F111.3 (PLAN T235) — captures the crossing track's title/artist into the HELD SignOn's own
    /// <see cref="HandoffContext"/> at straddle plan time (the F92.2 immutable-capture pattern this
    /// whole record already follows — see its own remarks — extended to a fact that is not knowable
    /// until the very unit that straddles it). A no-op when no SignOn is pending: <see cref="Orchestrator.GetNextAsync"/>'s
    /// straddle branch calls this only for a crossing SignOff-headed straddle whose reconciliation left
    /// the ceremony unchanged (see that branch's own remarks), including the one-sided F92.3 "into
    /// music-only" shape, which has nothing to enrich.
    ///
    /// <para>
    /// <b>Peek then Enqueue — two separate lock acquisitions, not one atomic operation</b> (T235 review
    /// finding F6, corrects an earlier version of this comment that called it "the SAME atomic
    /// supersede-by-(kind, discriminator) path"; it re-uses that supersede's KEY, never its atomicity —
    /// <see cref="SpeechDeferralQueue.Peek"/> and <see cref="SpeechDeferralQueue.Enqueue"/> each take
    /// and release the queue's lock independently). This read-then-write is safe here ONLY because the
    /// SignOn slot has exactly one OTHER writer — <see cref="ArmAsync"/> — and both it and this method
    /// run on the SAME feeder thread, one <see cref="Orchestrator.GetNextAsync"/> pull at a time, never
    /// concurrently with each other or with this method. Nothing inside <see cref="SpeechDeferralQueue"/>
    /// itself enforces that; it is an Orchestrator-side invariant (single-writer-thread), not a
    /// queue-side guarantee.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="SpeechDeferral.NotBefore"/> rides along unchanged (round-2 review finding F6).</b>
    /// This re-Enqueue is the SAME supersede-by-key path every other caller uses — an omitted
    /// <c>notBefore:</c> argument defaults to <see langword="null"/> and would silently drop any gate
    /// already sitting on the peeked <paramref name="crossingTrack"/>'s slot. Not reachable with a
    /// non-null gate TODAY (<see cref="Orchestrator.GetNextAsync"/>'s own straddle branch calls this
    /// BEFORE <see cref="HoldSignOnPastQueuedTail"/> ever runs on this same slot — the two
    /// straddle/decline paths never interleave on one SignOn), but passing it through costs nothing
    /// and removes a landmine for whichever future call ordering changes that invariant.
    /// </para>
    /// </summary>
    public void CaptureCrossingTrack(MediaItem crossingTrack)
    {
        if (deferralQueue.Peek(SpeechDeferralKind.SignOn) is not { } signOn || signOn.Handoff is not { } handoff)
            return; // one-sided straddle (SignOff only, F92.3) — no SignOn to enrich

        deferralQueue.Enqueue(
            SpeechDeferralKind.SignOn,
            signOn.Reason,
            signOn.Due,
            handoff with { CrossingTrackTitle = crossingTrack.Title, CrossingTrackArtist = crossingTrack.Artist },
            notBefore: signOn.NotBefore);
    }

    /// <summary>
    /// SPEC F124.1/F124.2 (PLAN T267) — holds a HELD SignOn's <see cref="SpeechDeferral.NotBefore"/>
    /// gate at least until the currently-queued tail (<paramref name="queuedAhead"/>, clamped to
    /// <see cref="boundaryBiasProvider"/>'s own F74.3 lookahead) has finished draining, from
    /// <paramref name="now"/> — the SAME instant the caller is already reading for this unit (SPEC
    /// F190.2: this method takes <paramref name="now"/> explicitly rather than reading a clock of its
    /// own, so arming a hold never races a second, independently-read "now" against the one the rest
    /// of the unit already committed to). A one-sided decline (SignOff only, F92.3) — no SignOn queued
    /// at all — is a no-op: there is nothing to hold.
    ///
    /// <para>
    /// <b>Why a separate gate at all</b> (round-1 review finding F1): the straddle hold-set
    /// <see cref="Orchestrator"/> passes to
    /// <see cref="SpeechDeferralQueue.TryDequeueDue"/> only ever excludes a deferral from a
    /// SINGLE such call, which a queued tail spanning several units (SPEC F124.6's own watch item —
    /// 230s queued at T-64s, several tracks' worth) outruns. Re-stamping
    /// <see cref="SpeechDeferral.Due"/> instead does NOT prevent that: a SignOn-headed fit's
    /// own <c>UntilBoundary</c> IS <c>Due - now</c>, so ANY drain instant a later pull computes
    /// already reaches at least <c>Due</c>, hold-set or not — the SignOn simply becomes the peeked
    /// fit on the very next pull and drains right back out through the ordinary due comparison, the
    /// hold having bought it nothing.
    /// </para>
    ///
    /// <para>
    /// <b>Due deliberately left untouched.</b> Only <see cref="SpeechDeferral.NotBefore"/> moves; the
    /// SignOn's own Due (when it is airable AT THE EARLIEST, absent a hold) stays exactly what
    /// <see cref="ArmAsync"/> computed it as — this method only ever pushes the EARLIEST-eligible
    /// instant later, never the ordering key <see cref="SpeechDeferralQueue.TryDequeueDue"/> sorts on.
    /// Due keeps meaning "the boundary this deferral belongs to", which is precisely how
    /// <see cref="ArmAsync"/>'s own reconcile/window-exit logic reads it (SPEC F124's round-1 review
    /// finding F2) — a re-stamped Due would make a live-held SignOn indistinguishable from one whose
    /// boundary genuinely moved.
    /// </para>
    ///
    /// <para>
    /// <b>The HONEST floor.</b> <c>notBefore</c> is the LATER of the estimated drain instant and
    /// whatever <see cref="SpeechDeferral.NotBefore"/> the SignOn already carried (<c>max()</c>, never
    /// a blind overwrite) — a second, shorter-tailed call must never PULL an existing, longer hold
    /// earlier than it already promised.
    /// </para>
    ///
    /// <para>
    /// <b>Peek then Enqueue</b> — the identical T235 finding F6 pattern <see cref="CaptureCrossingTrack"/>
    /// documents, safe here for the same single-feeder-thread reason.
    /// </para>
    ///
    /// <para>
    /// <b>The GATE is clamped to <see cref="IBoundaryBiasProvider.Current"/></b> (round-2 review
    /// finding F5) — the same F74.3 lookahead window <see cref="ArmAsync"/> itself arms within, so a
    /// held SignOn is never pushed further out than the window that justified holding it at all.
    /// </para>
    /// </summary>
    public void HoldSignOnPastQueuedTail(DateTimeOffset now, TimeSpan queuedAhead)
    {
        if (deferralQueue.Peek(SpeechDeferralKind.SignOn) is not { } signOn)
            return; // one-sided decline (SignOff only, F92.3) — no SignOn to hold

        var clampedQueuedAhead = queuedAhead < boundaryBiasProvider.Current ? queuedAhead : boundaryBiasProvider.Current;
        var estimatedDrain = now + clampedQueuedAhead;
        var notBefore = signOn.NotBefore is { } existing && existing > estimatedDrain ? existing : estimatedDrain;

        deferralQueue.Enqueue(
            SpeechDeferralKind.SignOn, signOn.Reason, signOn.Due, signOn.Handoff, notBefore: notBefore);
    }

    /// <summary>
    /// SPEC F92.1/F92.3 (STORY-243, PLAN T124): arms the two-piece handoff ceremony once
    /// <paramref name="scheduleResolver"/>'s resolved <c>OnAirSnapshot.BoundaryAt</c> enters
    /// <paramref name="boundaryBiasProvider"/>'s F74.3 lookahead window — the SAME window
    /// <see cref="Orchestrator.GetNextAsync"/> already reads to build the fit
    /// <see cref="MusicSelectionPolicy.SelectMusicCandidateAsync"/> consumes, so "in window" means one
    /// thing station-wide. A <see langword="null"/> <paramref name="scheduleResolver"/> (no format-clock
    /// schedule wired, the pre-F91 station shape) makes this a permanent no-op — logged with ONE WARN
    /// on the very first unit (T124 review finding F7) so that inert case is never silent, then never
    /// again for the life of this producer.
    ///
    /// <para>
    /// <b>Arm once per key, never every unit (T124 review finding F2 — the double-sign-off bug this
    /// fixes):</b> this producer runs on EVERY unit while a boundary sits in-window, but it only ever
    /// ACTS the first time it sees a given <c>(BoundaryAt, outgoing persona id, incoming persona id,
    /// outgoing show id, incoming show id)</c> key (the show-id pair widened this at F116.2/PLAN T248
    /// — see <see cref="lastArmedHandoff"/>'s own remarks) — <see cref="lastArmedHandoff"/> remembers
    /// the last one it armed or cleared for, and an unchanged key returns immediately, touching
    /// neither <paramref name="deferralQueue"/> nor <paramref name="personaStore"/> again. Without
    /// this, re-running the OLD unconditional enqueue-every-unit logic on a seam landing in
    /// <c>[BoundaryAt - SignOffLeadTime, BoundaryAt)</c> would: drain SignOff at this unit (its due
    /// has arrived) — see it drain, then IMMEDIATELY re-<see cref="SpeechDeferralQueue.Enqueue"/> a
    /// FRESH SignOff for the very same boundary with a due time that is now itself already in the
    /// past (the resolver's "current" segment has not yet flipped, so <c>BoundaryAt</c>/the persona
    /// ids still read identically) — which the NEXT unit's drain would fire AGAIN, a second sign-off
    /// airing for one boundary. The two elapsed-due guards below (skip arming SignOff once
    /// <c>BoundaryAt - SignOffLeadTime &lt;= now</c>; skip arming SignOn once <c>BoundaryAt &lt;=
    /// now</c>) are the belt to this key-check's suspenders: a piece is never handed to
    /// <see cref="SpeechDeferralQueue.Enqueue"/> with a due time that has already elapsed, full stop,
    /// even on the very first unit a key is ever seen.
    /// </para>
    ///
    /// <para>
    /// A CHANGED key — the common case is a schedule write moving the boundary or reassigning a
    /// show, or the resolver's own "current" segment finally flipping to the incoming one once
    /// <c>now</c> passes the old boundary — re-arms fresh: <see cref="SpeechDeferralQueue.Enqueue"/>'s
    /// own supersede-by-kind (SPEC F74.2) discards whatever the OLD key left pending of the same
    /// kind, and this method's own <c>ClearCeremony</c> local retracts anything the old key armed
    /// that the new one has no replacement for (window exit, gap-to-gap, self-handoff — see the
    /// dedupe list below). Nothing here is left to expire on its own.
    /// </para>
    ///
    /// <para>
    /// <b>Why <c>EnqueuePatterAsync</c> (now on <see cref="BreakPlanner"/>) calls this AFTER the deferral drain, not before (T124
    /// review finding):</b> the resolver's <c>OnAirSnapshot</c> is defined by wall-clock "now," so the
    /// instant "now" reaches an already-armed boundary, <c>ResolveAsync</c>'s own idea of the CURRENT
    /// segment flips to the INCOMING one — which makes the boundary THIS method would compute next
    /// (that new segment's own end) look far away, outside the window. Calling this before the drain
    /// would clear the very SignOff/SignOn deferrals the drain was about to fire, in the same pass
    /// they finally became due — a real defect this ordering fixes: drain whatever a PRIOR unit armed
    /// first, THEN decide what (if anything) to arm or clear for what comes next. A genuine schedule
    /// WRITE that moves a boundary away is unaffected — it is detected and cleared on some EARLIER
    /// unit, well before the (moved) due time would ever have elapsed.
    /// </para>
    ///
    /// <para>
    /// <b>Dedupe (SPEC F92.3, the T119-review build clarification; amended by F114.3/F116.2, PLAN
    /// T248):</b> the resolver's own <c>BoundaryAt</c>/<c>NextSegment</c> stay row-accurate even
    /// across a same-persona adjacency — THIS method is where "no ceremony airs" (or "airs as a
    /// one-piece transition") for that case is decided, never the resolver. Six shapes, by
    /// outgoing/incoming persona id and — for the equal-persona case only — show id (compared, never
    /// the display <c>Name</c>, per <c>ScheduleSegment.ShowId</c>'s own "write-authoritative identity
    /// field" ruling — a show rename can never look like a show change this way):
    /// <list type="bullet">
    /// <item>both persona ids null (a genuine gap, or a gap followed by an explicit
    /// persona-less/music-only scheduled segment) — gap-to-gap: nothing airs.</item>
    /// <item>persona ids equal and non-null, AND show ids equal (both named the SAME show, or both
    /// showless — the F91.6 seeded grid's own midnight roll is the showless instance) — self-handoff:
    /// nothing airs (F92.3 as amended by F114.3).</item>
    /// <item>persona ids equal and non-null, but show ids DIFFER (F114.3/F116.2) — a real boundary for
    /// ceremony purposes, but exactly ONE piece airs: the incoming sign-on, styled as a transition
    /// (the F92.4 incoming-welcome rung as designed behavior here, not a degrade) — no SignOff at all
    /// (there is no OTHER persona to hand off to), <see cref="HandoffContext.CounterpartName"/> null,
    /// <see cref="HandoffContext.ShowName"/>/<see cref="HandoffContext.ShowFlavor"/> the incoming
    /// show's own name/flavor.</item>
    /// <item>outgoing non-null, incoming null — SignOff only, <see cref="HandoffContext.CounterpartName"/>
    /// null ("the music keeps rolling"); <see cref="HandoffContext.ShowName"/> the ending show, if
    /// any (F114.3 — sign-off may still name the show it is closing out).</item>
    /// <item>outgoing null, incoming non-null — SignOn only, <see cref="HandoffContext.CounterpartName"/>
    /// null ("no predecessor"); <see cref="HandoffContext.ShowName"/>/<see cref="HandoffContext.ShowFlavor"/>
    /// the incoming show, if any.</item>
    /// <item>both non-null and different persona — both pieces, each naming the OTHER persona
    /// (<see cref="HandoffContext.CounterpartName"/>); F116.2's show-awareness rides EVERY shape ABOVE
    /// this one too, always via <c>OnAirSnapshot.Show</c>/<c>OnAirSnapshot.NextSegment</c>'s own
    /// <c>Show</c> (SPEC F116.1's chokepoint — never re-derived, never re-queried), never gated on
    /// whether a show happens to be assigned: an unnamed block simply carries null show fields
    /// straight through, so a showless station's ceremony stays byte-identical to pre-F116 (SPEC
    /// F116.1's own test).</item>
    /// </list>
    /// <see cref="HandoffContext.ShowFlavor"/> is captured on the SIGN-ON half only (F116.2 names
    /// flavor for the sign-on prompt alone); <see cref="HandoffContext.CounterpartShowName"/> is
    /// captured on the SIGN-OFF half only (F114.3's "may name the ending show and the next" — the
    /// "next" is the counterpart's show). Both stay prompt-only forever (SPEC F115.3) — this method
    /// never logs either.
    /// </para>
    ///
    /// <para>
    /// A persona id present on the schedule row but unresolvable through <paramref name="personaStore"/>
    /// (deleted out of band) degrades that HALF to "no DJ" (never-throws, SPEC F12.4) — the OTHER
    /// half still enqueues if it has one; see <see cref="ResolveHandoffPersonaAsync"/>.
    /// </para>
    /// </summary>
    public async Task ArmAsync(StationIdentity identity, DateTimeOffset now, CancellationToken ct)
    {
        if (scheduleResolver is null)
        {
            if (!scheduleResolverMissingWarned)
            {
                scheduleResolverMissingWarned = true;
                logger.LogWarning(
                    "No CachingScheduleResolver wired — the handoff ceremony producer (SPEC F92.1) is " +
                    "a permanent no-op for this process (no format-clock schedule in play, the pre-F91 " +
                    "station shape). Logged once.");
            }
            return;
        }

        // Clears both SignOff/SignOn (SPEC F92.1 revisit) — the one action every "no ceremony airs"
        // branch below shares, so the dedupe matrix in this method's own remarks reads as a matrix of
        // conditions rather than five repeated two-line clear blocks (T124 review simplify).
        //
        // ClearStale, never the blind Clear (round-1 review finding F2): every call site below is this
        // producer concluding "no ceremony belongs here any more" off the SCHEDULE's current state,
        // which says nothing about whether a queue-crossing SignOff already handed its paired SignOn to
        // this class's own HoldSignOnPastQueuedTail (SPEC F124.1) and is still waiting on that tail to
        // drain. A held-but-not-yet-airable SignOn (NotBefore in the future) is LIVE, not stale —
        // this producer re-evaluates every unit (by design, a schedule write must be noticed promptly),
        // so the window-exit branch below fires the very first unit real wall-clock time crosses the
        // boundary, typically well before a multi-minute queued tail has actually finished airing;
        // wiping the hold there destroyed the sign-on outright (round-1's reproduced F2 defect: it
        // survives past the boundary, then this exact call silently erases it, and the incoming DJ
        // never signs on). SignOff is never held (only a SignOn ever gets a NotBefore), so this is a
        // no-op difference for every SignOff clear below — always "stale," exactly what Clear already
        // removed.
        void ClearCeremony()
        {
            deferralQueue.ClearStale(SpeechDeferralKind.SignOff);
            deferralQueue.ClearStale(SpeechDeferralKind.SignOn);
        }

        var onAir = await scheduleResolver.ResolveAsync(ct);
        var boundaryAt = onAir.BoundaryAt;
        var untilBoundary = boundaryAt is { } b ? b - now : (TimeSpan?)null;

        if (boundaryAt is null || untilBoundary is not { } gap || gap <= TimeSpan.Zero || gap > boundaryBiasProvider.Current)
        {
            if (lastArmedHandoff is not null)
            {
                ClearCeremony();
                lastArmedHandoff = null;
            }
            return;
        }

        var outgoingId = onAir.PersonaId;
        var incomingId = onAir.NextSegment?.PersonaId;

        // SPEC F114.3/F116.2 (PLAN T248): ShowId is the write-authoritative identity field
        // (ScheduleSegment's own remarks) — compared here, never the display Name, so a rename can
        // never look like a show change. onAir.Segment is the SAME row onAir.PersonaId was read off;
        // onAir.NextSegment is the resolver's own next-boundary row (SPEC F116.1's chokepoint).
        var outgoingShowId = onAir.Segment?.ShowId;
        var incomingShowId = onAir.NextSegment?.ShowId;
        var tuple = (boundaryAt.Value, outgoingId, incomingId, outgoingShowId, incomingShowId);

        // Arm-once (T124 review finding F2): this exact tuple was already armed/cleared by a prior
        // unit — nothing has changed, so touch neither the queue nor personaStore again.
        if (lastArmedHandoff == tuple) return;
        lastArmedHandoff = tuple;

        if (outgoingId is null && incomingId is null)
        {
            ClearCeremony(); // gap-to-gap
            return;
        }

        if (outgoingId is not null && outgoingId == incomingId)
        {
            if (outgoingShowId == incomingShowId)
            {
                // F92.3 as amended by F114.3: same persona AND same show (or both showless) on a
                // row-accurate boundary airs no ceremony at all — never even attempted, so this never
                // shows up as a "drop" either.
                ClearCeremony(); // self-handoff
                return;
            }

            // F116.2: same persona, DIFFERENT show — a real boundary for ceremony purposes, but airs
            // exactly ONE piece: the incoming sign-on, styled as a transition (the F92.4
            // incoming-welcome rung as designed behavior here, not a degrade). There is no OTHER
            // persona to hand off to, so no SignOff is ever enqueued for this shape.
            deferralQueue.Clear(SpeechDeferralKind.SignOff);

            var transitionPersona = await ResolveHandoffPersonaAsync(incomingId, identity.Voice, ct);
            if (transitionPersona is null || boundaryAt.Value <= now)
            {
                // Round-2 review finding F3: ClearStale, never the blind Clear — the SAME reasoning
                // ClearCeremony's own remarks give (a live-held SignOn's NotBefore gate has not opened
                // against REAL wall-clock time, so it is not stale merely because THIS producer has
                // concluded nothing new belongs in this slot).
                deferralQueue.ClearStale(SpeechDeferralKind.SignOn);
            }
            else
            {
                // Round-2 review finding F4: a live hold on the SignOn slot must survive being
                // overwritten here — see this branch's sibling below (the two-DJ handoff re-arm) for
                // the full ruling; both call sites share the identical carry-forward.
                var held = deferralQueue.Peek(SpeechDeferralKind.SignOn);
                var heldNotBefore = held?.NotBefore;

                // SPEC F189.5 (PLAN T527, AC8): a re-arm that keeps the SAME incoming persona (this
                // branch, by construction — outgoingId == incomingId) reuses the ALREADY-HELD
                // snapshot rather than resolving again, so the counterpart's rendered voice never
                // drifts across a re-arm that only changed the show. A held snapshot for a DIFFERENT
                // persona (a genuine incoming-persona change elsewhere) is never reused.
                var speaker = held?.Handoff?.Speaker is { } heldSpeaker && heldSpeaker.PersonaId == incomingId
                    ? heldSpeaker
                    : await ForPersonaOrNullAsync(incomingId, ct);

                deferralQueue.Enqueue(
                    SpeechDeferralKind.SignOn,
                    "handoff: same-persona show transition (SPEC F116.2)",
                    boundaryAt.Value,
                    new HandoffContext(
                        transitionPersona.Value.Voice,
                        transitionPersona.Value.Name,
                        CounterpartName: null, // no OTHER DJ to name — it is the same persona
                        ShowName: onAir.NextSegment?.Show?.Name,
                        ShowFlavor: onAir.NextSegment?.Show?.Flavor,
                        Speaker: speaker),
                    notBefore: heldNotBefore);
            }

            return;
        }

        var outgoing = await ResolveHandoffPersonaAsync(outgoingId, identity.Voice, ct);
        var incoming = await ResolveHandoffPersonaAsync(incomingId, identity.Voice, ct);

        // Never hand the queue a piece whose due time has already elapsed (T124 review finding F2's
        // belt-and-suspenders guard) — a boundary can enter the window with less than SignOffLeadTime
        // left on the clock, in which case the SignOff half is simply skipped, never armed stale.
        var signOffDue = boundaryAt.Value - Orchestrator.SignOffLeadTime;
        if (outgoing is null || signOffDue <= now)
        {
            deferralQueue.Clear(SpeechDeferralKind.SignOff);
        }
        else
        {
            deferralQueue.Enqueue(
                SpeechDeferralKind.SignOff,
                "handoff: boundary entered the F74.3 window",
                signOffDue,
                new HandoffContext(
                    outgoing.Value.Voice, outgoing.Value.Name, incoming?.Name,
                    ShowName: onAir.Show?.Name,
                    CounterpartShowName: onAir.NextSegment?.Show?.Name,
                    Speaker: await ForPersonaOrNullAsync(outgoingId, ct)));
        }

        if (incoming is null || boundaryAt.Value <= now)
        {
            // Round-2 review finding F3: ClearStale, never the blind Clear — identical reasoning to
            // ClearCeremony's own remarks and this method's same-persona-transition branch above.
            deferralQueue.ClearStale(SpeechDeferralKind.SignOn);
        }
        else
        {
            // Round-2 review finding F4: a live hold on the CURRENT SignOn slot —
            // HoldSignOnPastQueuedTail's own NotBefore gate, still waiting on a queued tail to drain —
            // must SURVIVE being overwritten by a re-arm for what this evaluation just decided is a
            // genuinely different boundary/persona pair (a schedule write, or simply the resolver's own
            // "current" segment rolling forward once real wall-clock time finally reaches the OLD
            // boundary while the hold is still open). Chosen over the alternative (refuse to re-arm at
            // all while a hold is live): refusing would leave the STALE, now-WRONG persona/boundary
            // content sitting in this slot until the old hold finally opens — audibly worse than airing
            // the CORRECT content a few seconds later than the bare gate alone would allow, since the
            // gate we DO carry forward still enforces "not before the already-queued tail finishes,"
            // exactly the same physical constraint that queued tail always represented, regardless of
            // which ceremony's content ends up occupying this slot. Peek-then-Enqueue is racy only in
            // theory here — same single-feeder-thread invariant <see cref="HoldSignOnPastQueuedTail"/>
            // and <see cref="CaptureCrossingTrack"/> already document.
            //
            // This never disturbs the F2 reconcile guard's own Due-equality semantics
            // (<see cref="Orchestrator.GetNextAsync"/>'s straddle branch, "reconciledSignOff.Due ==
            // pending.Due"): that check reads the SignOff half only, which this branch's SignOff arm
            // (above) still stamps with the fresh, correctly-computed Due for whatever boundary this
            // evaluation resolved — carrying NotBefore forward on the SignOn half changes nothing
            // about what Due that guard ever sees.
            var held = deferralQueue.Peek(SpeechDeferralKind.SignOn);
            var heldNotBefore = held?.NotBefore;

            // SPEC F189.5 (PLAN T527, AC8) — same reuse rule as the same-persona transition branch
            // above: a held snapshot for the SAME incoming persona survives the re-arm untouched.
            var speaker = held?.Handoff?.Speaker is { } heldSpeaker && heldSpeaker.PersonaId == incomingId
                ? heldSpeaker
                : await ForPersonaOrNullAsync(incomingId, ct);

            deferralQueue.Enqueue(
                SpeechDeferralKind.SignOn,
                "handoff: boundary entered the F74.3 window",
                boundaryAt.Value,
                new HandoffContext(
                    incoming.Value.Voice, incoming.Value.Name, outgoing?.Name,
                    ShowName: onAir.NextSegment?.Show?.Name,
                    ShowFlavor: onAir.NextSegment?.Show?.Flavor,
                    Speaker: speaker),
                notBefore: heldNotBefore);
        }
    }

    /// <summary>
    /// Resolves one half of a handoff (SPEC F92.2) from <paramref name="personaStore"/> — never
    /// throws (F12.4): a null <paramref name="personaId"/> (no DJ on this side), an unwired
    /// <paramref name="personaStore"/>, a missing row (deleted out of band), or any store fault all
    /// degrade to <see langword="null"/>, which <see cref="ArmAsync"/> treats as "this half is
    /// music-only" (SPEC F92.3). Voice mirrors <c>ResolvePersonaAsync</c> (now on <see cref="BreakPlanner"/>)'s own
    /// empty-sentinel rule: the persona's own voice when set, else <paramref name="stationVoice"/>.
    /// </summary>
    async Task<(string Voice, string Name)?> ResolveHandoffPersonaAsync(
        long? personaId, string stationVoice, CancellationToken ct)
    {
        if (personaId is null || personaStore is null) return null;

        try
        {
            var persona = await personaStore.GetByIdAsync(personaId.Value, ct);
            if (persona is null)
            {
                logger.LogWarning(
                    "Handoff boundary names persona id={PersonaId} with no matching persona row — " +
                    "treating that half as music-only (SPEC F92.3 degrade).",
                    personaId);
                return null;
            }

            return (VoiceOf(persona, stationVoice), persona.Name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to resolve handoff persona id={PersonaId} — treating that half as music-only (F12.4).",
                personaId);
            return null;
        }
    }

    /// <summary>
    /// SPEC F189.5 (PLAN T527): resolves the <see cref="SpeakerSnapshot"/> one arm-time half of a
    /// handoff carries — never throws, the SAME degrade family <see cref="ResolveHandoffPersonaAsync"/>
    /// applies immediately above (a null <paramref name="personaId"/>, no source wired, or any source
    /// fault all degrade to <see langword="null"/>, WARN-logged on a fault). Speaker resolution reads a
    /// genuinely different seam than <see cref="ResolveHandoffPersonaAsync"/>'s own Voice/Name (the
    /// persona CARD here, the persona ROW there) — the two ARE captured from different seams, and they
    /// CAN diverge (two shipped data states prove it: F79.4's blanked import voice; the card-less
    /// default persona <c>PersonaCardMigrator.EnsureDefaultPersonaAsync</c> writes). Round-2 review
    /// finding F3: <c>BreakPlanner.BuildHandoffRequest</c> is where that gets resolved — it aligns
    /// this snapshot's Voice to <see cref="HandoffContext.Voice"/> (the row's) when it builds the
    /// drained request, so a render never sees the two disagree.
    /// </summary>
    async Task<SpeakerSnapshot?> ForPersonaOrNullAsync(long? personaId, CancellationToken ct)
    {
        if (personaId is null || speakerSnapshots is null) return null;

        try
        {
            return await speakerSnapshots.ForPersonaAsync(personaId.Value, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to resolve handoff persona id={PersonaId}'s speaker snapshot — this half arms without one (SPEC F189.6-adjacent degrade).",
                personaId);
            return null;
        }
    }

    /// <summary>
    /// The empty-sentinel voice rule <c>ResolvePersonaAsync</c> (now on <see cref="BreakPlanner"/>) and
    /// <see cref="ResolveHandoffPersonaAsync"/> both apply (SPEC F35.2/F92.2): a persona's own
    /// <see cref="Persona.Voice"/> when set, else <paramref name="stationVoice"/> — <c>""</c> is
    /// <see cref="Persona"/>'s own documented "use the station's default" sentinel, never "unset".
    /// </summary>
    static string VoiceOf(Persona persona, string stationVoice) =>
        string.IsNullOrEmpty(persona.Voice) ? stationVoice : persona.Voice;
}
