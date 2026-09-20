namespace GenWave.Orchestration;

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;

/// <summary>
/// PLAN T536 (SPEC F192) — turns one <see cref="BreakPlan"/> and the <see cref="SlotOutcome"/> list
/// <see cref="BreakRenderer"/> returned for it into a <see cref="BreakOutcome"/>: the rendered
/// survivors, stamped exactly as today (DJ-name attribution, an announcement's <see cref="AnnouncementMediaId.Wrap"/>),
/// and one settled <see cref="ReservationOutcome"/> per reservation the plan claimed. Extracted off
/// <see cref="Orchestrator"/> (formerly <c>Orchestrator.Render.cs</c>'s <c>DeliverOutcome</c>/
/// <c>DeliverRendered</c>/<c>ReportDrop</c>) so the buffer-facing seam a spec drives directly never
/// needs a live TTS backend, a fake clock, or a real Orchestrator — only a hand-built plan and
/// outcome list (SPEC F192, STORY-459 AC1-AC8).
///
/// <para>
/// <b>Holds no buffer (SPEC F192.6, AC6).</b> <see cref="Deliver"/> returns <see cref="BreakOutcome.Items"/>
/// for the CALLER to enqueue — <see cref="Orchestrator"/> is the only class that ever touches
/// <c>Queue&lt;MediaItem&gt;</c>, unchanged from before this extraction.
/// </para>
///
/// <para>
/// <b>The Wrap's new home (T534 review carry-forward).</b> <see cref="BreakRenderer"/> used to stamp
/// <see cref="AnnouncementMediaId.Wrap"/> onto a freshly-rendered verbatim segment itself; F192.1
/// assigns that stamp here instead, alongside the DJ-name stamp it always sat next to on
/// <see cref="Orchestrator"/> — one class owns every post-render stamp, not two.
/// </para>
/// </summary>
public sealed class BreakDelivery(
    ILogger<BreakDelivery> logger,
    IStationEventSink events,
    IPatterDurationEstimator patterEstimator)
{
    /// <summary>
    /// Walks <paramref name="plan"/>'s slots and <paramref name="outcomes"/> together, in ordinal
    /// order (SPEC F192.1): a rendered slot is stamped and joins <see cref="BreakOutcome.Items"/>; a
    /// dropped slot is reported per its <see cref="DropPolicy"/> (SPEC F192.2); every reservation
    /// settles exactly once (SPEC F192.4).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="outcomes"/> has a different length than <paramref name="plan"/>'s own slot
    /// list (T534 review carry-forward) — a silent mis-pair here means a wrong announcement id in a
    /// drop WARN and a wrong DJ-name stamp, so this fails loudly instead.
    /// </exception>
    public BreakOutcome Deliver(BreakPlan plan, IReadOnlyList<SlotOutcome> outcomes)
    {
        if (outcomes.Count != plan.Slots.Count)
        {
            throw new ArgumentException(
                $"BreakPlan has {plan.Slots.Count} slot(s) but {outcomes.Count} outcome(s) were " +
                "supplied — a length mismatch means a slot and its outcome are silently mis-paired.",
                nameof(outcomes));
        }

        var items = new List<MediaItem>(plan.Slots.Count);
        var reservations = new Dictionary<Reservation, ReservationOutcome>();

        for (var i = 0; i < plan.Slots.Count; i++)
            DeliverSlot(plan, plan.Slots[i], outcomes[i], items, reservations);

        return new BreakOutcome(items, reservations);
    }

    // Hoisted off Deliver's own for-loop (PLAN T536 review Notes — Deliver's body ran 49 lines, over
    // the ~30-line guideline): the walk-every-slot loop stays on Deliver itself, this method owns one
    // slot's own outcome dispatch (SPEC F192.1/F192.2/F192.4) — a rendered slot is stamped and joins
    // items; a dropped slot is reported per its DropPolicy; either way its reservation (if any)
    // settles exactly once.
    void DeliverSlot(
        BreakPlan plan, PlannedSlot slot, SlotOutcome outcome,
        List<MediaItem> items, Dictionary<Reservation, ReservationOutcome> reservations)
    {
        void Settle(ReservationOutcome result)
        {
            if (slot.Reservation is { } reservation)
                reservations[reservation] = result;
        }

        switch (outcome)
        {
            case RenderedOutcome rendered:
                items.Add(DeliverRendered(plan, slot, rendered.Item));
                Settle(new AiredReservation());
                break;
            case TimedOutOutcome:
                ReportDrop(slot, "render budget exceeded");
                Settle(new DroppedReservation("render budget exceeded"));
                break;
            case FailedOutcome failed:
                var cause = failed.Exception is not null ? "render faulted" : "render returned null";
                ReportDrop(slot, cause);
                Settle(new DroppedReservation(cause));
                break;
            case AbandonedOutcome:
                // Not yet produced by BreakRenderer (PLAN T536/SPEC F192) — the vend that
                // abandoned it already logged its own cause, so no drop report fires here.
                Settle(new DroppedReservation("not attempted"));
                break;
            case SlotOutcome unhandled:
                throw new UnreachableException($"Unhandled {nameof(SlotOutcome)} case: {unhandled.GetType()}");
        }
    }

    /// <summary>
    /// SPEC F192.5's reservation-abandonment clause, as a value: every reservation <paramref name="plan"/>
    /// claimed settles <see cref="AbandonedReservation"/> and <see cref="BreakOutcome.Items"/> comes
    /// back empty, with nothing logged (a cancelled unit is not a render failure any WARN would name
    /// a cause for). Round-2 review finding F2: <c>RenderPlanAsync</c>'s own cancellation check — the
    /// buffer-side half of F192.5 this SPEC clause also covers — does not call this method today; the
    /// method returns a <see cref="BreakOutcome"/> that plain `Task`-returning caller has nowhere to
    /// send, so calling it there would only build a value and discard it. This is record-only for
    /// now: exercised directly by <c>AbandonsEveryReservation</c> (STORY-459 AC9), with no production
    /// consumer until a later story gives reservation settlement somewhere to go.
    /// </summary>
    public BreakOutcome Abandon(BreakPlan plan)
    {
        var reservations = new Dictionary<Reservation, ReservationOutcome>();
        foreach (var slot in plan.Slots)
        {
            if (slot.Reservation is { } reservation)
                reservations[reservation] = new AbandonedReservation();
        }

        return new BreakOutcome([], reservations);
    }

    MediaItem DeliverRendered(BreakPlan plan, PlannedSlot slot, MediaItem item)
    {
        if (slot.ObserveDuration && item.DurationMs is int measuredMs)
        {
            var request = RequestOf(slot.Source);
            patterEstimator.ObserveRendered(
                slot.Kind, request.PersonaName, request.Voice, TimeSpan.FromMilliseconds(measuredMs), request.ShowName);
        }

        if (slot.Kind is SegmentKind.StationId or SegmentKind.Announcement or SegmentKind.Ad)
            item = item with { DjName = plan.Context.UnitDjName };

        if (slot.Kind == SegmentKind.Announcement)
            item = item with { MediaId = AnnouncementMediaId.Wrap(AnnouncementIdOf(slot), item.MediaId) };

        return item;
    }

    // SPEC F186.3's per-kind drop-report assignment, dispatched off the slot's own DropPolicy (T521's
    // DropPolicyFor already decided WHICH policy applies at plan time) — exhaustive without a discard
    // arm, the SAME closed-hierarchy idiom BreakPlan.ToTrace() uses. Unlike its pre-T536 shape, the
    // WarnDrop arms below dispatch on the policy's own Subject (SPEC F192.2) rather than re-deriving
    // SegmentKind.ContextSegment — WarnDrop now carries which line to log, not this switch. Subject
    // is a required record component (PLAN T536 review finding F4), so this switch is exhaustive over
    // exactly two live WarnDrop shapes — no third "no Subject" arm to ever reach.
    void ReportDrop(PlannedSlot slot, string cause)
    {
        switch (slot.Drop)
        {
            case SilentDrop:
                break;
            case WarnAndEventDrop:
                LogHandoffDrop(slot.Kind, cause);
                break;
            case WarnDrop { Subject: ContextProviderWarnSubject context }:
                LogContextSegmentDrop(context.ProviderKey, cause);
                break;
            case WarnDrop { Subject: AnnouncementWarnSubject }:
                LogAnnouncementDrop(AnnouncementIdOf(slot), cause);
                break;
            case DropPolicy:
                throw new UnreachableException($"Unhandled {nameof(DropPolicy)} case: {slot.Drop.GetType()}");
        }
    }

    static SegmentRequest RequestOf(SlotSource source) => source switch
    {
        RenderSource render => render.Request,
        VerbatimSource verbatim => verbatim.Request,
        ReadySource => throw new UnreachableException($"{nameof(ReadySource)} never observes duration"),
        SlotSource => throw new UnreachableException($"Unhandled {nameof(SlotSource)} case: {source.GetType()}"),
    };

    static long AnnouncementIdOf(PlannedSlot slot) =>
        slot.Reservation is { Kind: ReservationKind.Announcement, Key: var key }
            ? long.Parse(key, CultureInfo.InvariantCulture)
            : throw new UnreachableException("Announcement slot without an Announcement reservation (SPEC F187.3)");

    /// <summary>
    /// SPEC F92.4: a handoff piece that failed to render (budget exceeded, faulted, or a null result
    /// — e.g. <c>TtsSegmentSource</c>'s own drop of non-LLM-authored handoff copy, PLAN T123)
    /// degrades that HALF of the ceremony only. WARN here, plus a booth-log entry via
    /// <see cref="events"/> (mirrors the <c>DegradationModeChanged</c>/<c>SegmentGenerated</c> event
    /// idiom <c>BoothLogWriter</c> already reacts to) so an operator sees it without grepping logs.
    /// The OTHER piece of the same boundary still airs if it rendered — this method only logs and
    /// publishes; it never touches <see cref="Deliver"/>'s own <c>items</c>/<c>reservations</c> or a
    /// caller's buffer itself — and the next boundary retries the full ceremony from scratch: nothing
    /// here latches a failure.
    /// </summary>
    void LogHandoffDrop(SegmentKind kind, string cause)
    {
        logger.LogWarning(
            "Handoff piece {Kind} dropped ({Cause}) — that half of the ceremony airs nothing; the " +
            "other piece still airs if it rendered, and the next boundary retries the full ceremony " +
            "(SPEC F92.4).",
            kind, cause);
        events.Publish(new HandoffPieceDropped(kind.ToString(), cause));
    }

    /// <summary>
    /// SPEC F107.6 (STORY-297, PLAN T224) — a context segment that failed to render (budget
    /// exceeded, faulted, or a null result — e.g. <c>TtsSegmentSource</c>'s own drop of non-LLM-
    /// authored context copy, mirroring PLAN T123's handoff precedent) never airs and never blocks
    /// music: WARN only, one line, naming the provider and cause (T224 review finding — the earlier
    /// shape named only the cause, leaving an operator unable to tell which provider dropped when
    /// more than one is configured; <paramref name="providerKey"/> is the SAME discriminator the
    /// Information-level freshness/blank-facts skips two calls up already name, threaded through the
    /// slot's own <see cref="WarnDrop.Subject"/> (a <see cref="ContextProviderWarnSubject"/>, SPEC
    /// F192.2) so this AFTER-render drop can name it too — see <see cref="BreakPlanner"/>'s own context
    /// segment construction site for where it rides in). No <see cref="events"/> publish —
    /// unlike <see cref="LogHandoffDrop"/>'s F92.4 booth-log entry, F107 defines no drop-specific
    /// booth-log event, and a render miss here is ordinary skip-never-silence operation (the SAME
    /// posture the drain arm's own freshness/blank-facts skips already log at Information one call
    /// up), not a ceremony half going dark. The next boundary's own drain simply gets another chance.
    /// </summary>
    void LogContextSegmentDrop(string? providerKey, string cause) =>
        logger.LogWarning(
            "Context segment for provider {ProviderKey} dropped ({Cause}) — no context item reaches " +
            "air this boundary; music continues, and the next drain retries (SPEC F107.6).",
            providerKey ?? "(unknown)", cause);

    /// <summary>
    /// SPEC F144.5 (STORY-358, PLAN T341) — an announcement segment that failed to render (budget
    /// exceeded, faulted, or a null result) never airs and never blocks music: WARN only, mirroring
    /// <see cref="LogContextSegmentDrop"/>'s own posture one method up — no booth-log entry (a later
    /// task's mark-aired/re-arm guardian owns that surface, reading <c>station.announcement</c>
    /// directly; this class only ever vends, never transitions the row). The claimed row itself is
    /// untouched by this drop — SPEC F144.5's own re-arm (claimed -&gt; pending after one break cycle
    /// with no air) is that guardian's job, not this log line's.
    ///
    /// <paramref name="announcementId"/> names WHICH claimed row dropped (T341 review finding F8 —
    /// the SAME <see cref="LogContextSegmentDrop"/> providerKey precedent immediately above: an
    /// operator staring at this WARN with more than one announcement claimed this unit needs to know
    /// which row is still sitting claimed, not merely that "an" announcement dropped).
    /// </summary>
    void LogAnnouncementDrop(long announcementId, string cause) =>
        logger.LogWarning(
            "Announcement {AnnouncementId} dropped ({Cause}) — the claimed row does not air this unit; " +
            "music continues (SPEC F144.5).",
            announcementId.ToString(CultureInfo.InvariantCulture), cause);
}
