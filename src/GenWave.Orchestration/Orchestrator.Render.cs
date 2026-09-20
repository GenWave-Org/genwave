namespace GenWave.Orchestration;

using System.Diagnostics;
using System.Globalization;
using GenWave.Core.Domain;

// PLAN T534 — the render phase: hands a BreakPlan built by BreakPlanner (SPEC F188) to BreakRenderer
// (SPEC F191) and then delivers the resulting per-slot outcomes — observing measured duration,
// DJ-name stamping, and enqueueing on a render, reporting a drop's cause otherwise. Split from
// Orchestrator.cs purely for the ~300-line budget (csharp-best-practices) — shares that file's
// ctor-level fields via the partial class.
//
// The kick-all-then-await race itself (SPEC F191.1-F191.3), KickSlot's dispatch, and the verbatim
// flavored/plain render moved onto BreakRenderer at T534 — this file keeps only what still needs the
// estimator/buffer/logger seams BreakRenderer is deliberately not given (PLAN T536/F192 finishes
// moving THOSE onto their own seam; consuming the returned outcome list in slot order is this task's
// own interim shape).
public sealed partial class Orchestrator
{
    /// <summary>
    /// Delivers <paramref name="plan"/> through <see cref="breakRenderer"/> (SPEC F191), then walks
    /// the returned outcomes in the SAME ordinal order the plan declared its slots, so the buffer
    /// keeps receiving items in that order regardless of which render happened to finish first.
    /// </summary>
    async Task RenderPlanAsync(BreakPlan plan, CancellationToken ct)
    {
        var outcomes = await breakRenderer.RenderAsync(plan, ct);

        for (var i = 0; i < plan.Slots.Count; i++)
            DeliverOutcome(plan, plan.Slots[i], outcomes[i]);
    }

    // Exhaustive without a discard arm (SPEC F187.1) — the SlotOutcome => throw arm is the same
    // closed-hierarchy idiom KickSlot/ReportDrop use for their own switches.
    void DeliverOutcome(BreakPlan plan, PlannedSlot slot, SlotOutcome outcome)
    {
        switch (outcome)
        {
            case RenderedOutcome rendered:
                DeliverRendered(plan, slot, rendered.Item);
                break;
            case TimedOutOutcome:
                ReportDrop(slot, "render budget exceeded");
                break;
            case FailedOutcome failed:
                ReportDrop(slot, failed.Exception is not null ? "render faulted" : "render returned null");
                break;
            case AbandonedOutcome:
                break; // not yet produced (PLAN T536/SPEC F192) — the vend that abandoned it already logged its own cause
            case SlotOutcome:
                throw new UnreachableException($"Unhandled {nameof(SlotOutcome)} case: {outcome.GetType()}");
        }
    }

    void DeliverRendered(BreakPlan plan, PlannedSlot slot, MediaItem item)
    {
        if (slot.ObserveDuration && item.DurationMs is int measuredMs)
        {
            var request = RequestOf(slot.Source);
            patterEstimator.ObserveRendered(
                slot.Kind, request.PersonaName, request.Voice, TimeSpan.FromMilliseconds(measuredMs), request.ShowName);
        }

        if (slot.Kind is SegmentKind.StationId or SegmentKind.Announcement or SegmentKind.Ad)
            item = item with { DjName = plan.Context.UnitDjName };

        buffer.Enqueue(item);
    }

    // SPEC F186.3's per-kind drop-report assignment, dispatched off the slot's own DropPolicy
    // (T521's DropPolicyFor already decided WHICH policy applies at plan time) — exhaustive without
    // a discard arm, the SAME closed-hierarchy idiom BreakPlan.ToTrace() uses. The WarnDrop arm below
    // still re-derives SegmentKind.ContextSegment to choose its log line, because WarnDrop itself
    // carries no text of its own to distinguish a context-segment drop from an announcement drop;
    // giving WarnDrop that text so this switch needs no kind check is deferred to T536/F192.2, not
    // done here.
    void ReportDrop(PlannedSlot slot, string cause)
    {
        switch (slot.Drop)
        {
            case SilentDrop:
                break;
            case WarnAndEventDrop:
                LogHandoffDrop(slot.Kind, cause);
                break;
            case WarnDrop when slot.Kind == SegmentKind.ContextSegment:
                LogContextSegmentDrop(slot.ContextProviderKey, cause);
                break;
            case WarnDrop:
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
}
