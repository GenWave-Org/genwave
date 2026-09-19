namespace GenWave.Orchestration;

using System.Diagnostics;
using System.Globalization;
using GenWave.Core.Domain;

// PLAN T522 — the render phase: consumes a BreakPlan built by BreakPlanner (SPEC F188) and kicks
// every slot's delivery, unchanged from EnqueuePatterAsync's own render-await loop (SPEC F186.3/
// F186.4) — kick every slot's render (or ready item) immediately, then await each in ORDINAL order
// (never completion order), so the buffer keeps receiving items in exactly the order the plan
// declared them, regardless of which render happens to finish first. Split from Orchestrator.cs
// purely for the ~300-line budget (csharp-best-practices) — shares that file's ctor-level fields via
// the partial class.
//
// Before T522 every kick except LeadIn started before EnqueueHandoffCeremonyAsync; now every kick
// starts only after it (PlanAndRenderAsync runs PlanAsync and then the handoff arm before this method
// kicks anything), so a real TTS render loses the arm's own scheduleResolver.ResolveAsync latency off
// its render-ahead window — an interim cost PR-6 removes, with no fake-clock spec effect.
public sealed partial class Orchestrator
{
    /// <summary>
    /// Kicks every slot in <paramref name="plan"/> immediately, then awaits each in ordinal order
    /// against the plan's own render budget (SPEC F186.3's "exceeds the render budget", the fake
    /// clock's own <c>Task.WhenAny(render, Task.Delay(budget, timeProvider))</c> race, unchanged) —
    /// a timed-out, faulted, or null render drops per the slot's own <see cref="PlannedSlot.Drop"/>
    /// policy (SPEC F186.4); a successful render (or an already-ready item) reaches the buffer,
    /// stamped with the unit DJ name for the three kinds that always have carried one
    /// (StationId/Announcement/Ad — SPEC F186's own imaging-not-DJ-content carve-out).
    /// </summary>
    async Task RenderPlanAsync(BreakPlan plan, CancellationToken ct)
    {
        // Kick order first (SPEC F187.2's ordinal already fixes it), await order second — this list
        // preserves both: LINQ's Select is lazy, but ToList below forces every KickSlot call to run
        // BEFORE the first await, so every render is already in flight before this method awaits any
        // of them (the "kick all" half of the interim loop, PLAN T522).
        var kicked = plan.Slots.Select(slot => (Slot: slot, Render: KickSlot(slot, ct))).ToList();

        foreach (var (slot, render) in kicked)
        {
            var winner = await Task.WhenAny(render, Task.Delay(plan.RenderBudget, timeProvider, ct));

            if (winner != render)
            {
                ReportDrop(slot, "render budget exceeded");
                continue; // timed out — the still-running render is left unawaited, unchanged behavior
            }

            if (render.IsCompletedSuccessfully && render.Result is { } segment)
            {
                if (slot.ObserveDuration && segment.DurationMs is int measuredMs)
                {
                    var request = RequestOf(slot.Source);
                    patterEstimator.ObserveRendered(
                        slot.Kind, request.PersonaName, request.Voice, TimeSpan.FromMilliseconds(measuredMs), request.ShowName);
                }

                if (slot.Kind is SegmentKind.StationId or SegmentKind.Announcement or SegmentKind.Ad)
                    segment = segment with { DjName = plan.Context.UnitDjName };

                buffer.Enqueue(segment);
            }
            else
            {
                ReportDrop(slot, render.IsFaulted ? "render faulted" : "render returned null");
            }
        }
    }

    // Exhaustive without a discard arm (SPEC F187.1) — the SlotSource => throw arm is the same
    // BreakPlan.ToTrace() idiom this hierarchy is declared alongside.
    Task<MediaItem?> KickSlot(PlannedSlot slot, CancellationToken ct) => slot.Source switch
    {
        RenderSource render => tts.RenderAsync(render.Request, ct),
        VerbatimSource verbatim => RenderVerbatimAsync(verbatim, slot, ct),
        ReadySource ready => Task.FromResult<MediaItem?>(ready.Item),
        SlotSource => throw new UnreachableException($"Unhandled {nameof(SlotSource)} case: {slot.Source.GetType()}"),
    };

    /// <summary>
    /// SPEC F191.4's verbatim law, moved here verbatim from EnqueuePatterAsync's own
    /// RenderAnnouncementAsync local: <see cref="VerbatimSource.AllowFlavor"/> true tries the
    /// announcement copy writer first, falling back to <see cref="VerbatimSource.Copy"/> on null or
    /// throw; false renders the plain copy directly. Every <see cref="VerbatimSource"/> slot is an
    /// Announcement (SPEC F188 — no other kind ever carries one), so the rendered id always wraps
    /// with the claimed announcement's own id (SPEC F144.1's carry requirement).
    /// </summary>
    async Task<MediaItem?> RenderVerbatimAsync(VerbatimSource verbatim, PlannedSlot slot, CancellationToken ct)
    {
        if (announcementRenderer is not { } renderer)
            return null;

        var flavoredText = verbatim.AllowFlavor
            ? await ResolveFlavoredAnnouncementCopyAsync(verbatim.Request, verbatim.Copy.Text, ct)
            : null;

        var copy = flavoredText is { } text ? verbatim.Copy with { Text = text } : verbatim.Copy;
        var rendered = await renderer.RenderAsync(verbatim.Request, copy, ct);

        return rendered is { } item
            ? item with { MediaId = AnnouncementMediaId.Wrap(AnnouncementIdOf(slot), item.MediaId) }
            : null;
    }

    // SPEC F186.3's per-kind drop-report assignment, dispatched off the slot's own DropPolicy
    // (T521's DropPolicyFor already decided WHICH policy applies at plan time) — exhaustive without
    // a discard arm, the SAME closed-hierarchy idiom BreakPlan.ToTrace() uses. The WarnDrop arm below
    // still re-derives SegmentKind.ContextSegment to choose its log line, because WarnDrop itself
    // carries no text of its own to distinguish a context-segment drop from an announcement drop;
    // giving WarnDrop that text so this switch needs no kind check is deferred to T534/F192.2, not
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
