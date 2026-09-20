namespace GenWave.Orchestration;

using System.Diagnostics;
using System.Globalization;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// PLAN T534 (SPEC F191) — turns a <see cref="BreakPlan"/> into one <see cref="SlotOutcome"/> per
/// slot, in ordinal order: kicks every slot's render (or ready item) immediately, then awaits each
/// against the plan's own render budget in ORDINAL order (never completion order), so a caller that
/// enqueues the results keeps receiving them in exactly the order the plan declared them, regardless
/// of which render happens to finish first (SPEC F191.1/F191.2, extracted from the T522 interim loop
/// that used to live on <see cref="Orchestrator"/> directly).
///
/// <para>
/// <b>The outcome-from-the-task-not-the-race fix (SPEC F191.3, T124 review finding F6).</b>
/// <see cref="ResolveOutcomeAsync"/> reads a completed slot's outcome from the render task's OWN
/// terminal state (<see cref="Task.IsCompleted"/>/<see cref="Task.IsCompletedSuccessfully"/>/
/// <see cref="Task.IsFaulted"/>/<see cref="Task{TResult}.Result"/>) after <see cref="Task.WhenAny(Task[])"/>
/// resolves — never from WHICH of the two race members <c>WhenAny</c> reports as the winner. A render
/// that completes at the exact instant its budget elapses can report either task as the winner
/// depending on scheduling order; reading the render's own state instead of the winner reference means
/// that tie always resolves to <see cref="RenderedOutcome"/>, not a spurious <see cref="TimedOutOutcome"/>.
/// </para>
///
/// This class takes ONLY the seams a render needs — <see cref="ITtsSegmentSource"/>, the clock, and
/// the two optional verbatim-announcement seams (SPEC F191.5) — no logger, no
/// <see cref="IStationEventSink"/>, no <see cref="IPatterDurationEstimator"/>, no buffer: it is
/// testable with a scripted TTS fake and a fake clock alone. Reporting a drop's cause, observing a
/// measured duration, DJ-name stamping, and enqueueing the result all stay on
/// <see cref="Orchestrator"/>, which consumes the returned outcome list in slot order (PLAN T536/
/// SPEC F192 finishes moving those onto their own seam).
/// </summary>
public sealed class BreakRenderer(
    ITtsSegmentSource tts,
    TimeProvider timeProvider,
    IVerbatimSegmentRenderer? announcementRenderer = null,
    IAnnouncementCopyWriter? announcementCopyWriter = null)
{
    /// <summary>
    /// Kicks every slot in <paramref name="plan"/> before the first await (SPEC F191.2), then awaits
    /// each in ordinal order against <see cref="BreakPlan.RenderBudget"/>, returning one
    /// <see cref="SlotOutcome"/> per slot in the SAME ordinal order.
    /// </summary>
    public async Task<IReadOnlyList<SlotOutcome>> RenderAsync(BreakPlan plan, CancellationToken ct)
    {
        // Kick order first (SPEC F191.2's ordinal already fixes it), await order second — this list
        // preserves both: LINQ's Select is lazy, but ToList below forces every KickSlot call to run
        // BEFORE the first await, so every render is already in flight before this method awaits any
        // of them.
        var kicked = plan.Slots.Select(slot => KickSlot(slot, ct)).ToList();

        var outcomes = new List<SlotOutcome>(kicked.Count);
        foreach (var render in kicked)
            outcomes.Add(await ResolveOutcomeAsync(render, plan.RenderBudget, ct));

        return outcomes;
    }

    /// <summary>
    /// Races <paramref name="render"/> against <paramref name="budget"/> (SPEC F191.3) and reads the
    /// outcome from <paramref name="render"/>'s own terminal state once IT is the one that resolved —
    /// see this class's own remarks for why that must never be "whichever task WhenAny reports as the
    /// winner".
    /// </summary>
    async Task<SlotOutcome> ResolveOutcomeAsync(Task<MediaItem?> render, TimeSpan budget, CancellationToken ct)
    {
        await Task.WhenAny(render, Task.Delay(budget, timeProvider, ct));

        if (!render.IsCompleted)
            return new TimedOutOutcome(); // the still-running render is left unawaited, unchanged behavior

        if (render.IsCompletedSuccessfully)
            return render.Result is { } item ? new RenderedOutcome(item) : new FailedOutcome();

        return new FailedOutcome(render.IsFaulted ? render.Exception?.GetBaseException() : null);
    }

    // Exhaustive without a discard arm (SPEC F187.1) — the SlotSource => throw arm is the same
    // BreakPlan.ToTrace() idiom this hierarchy is declared alongside.
    Task<MediaItem?> KickSlot(PlannedSlot slot, CancellationToken ct) => slot.Source switch
    {
        RenderSource render => tts.RenderAsync(render.Request, ct),
        VerbatimSource verbatim => RenderVerbatimAsync(verbatim, slot, ct),
        ReadySource ready => Task.FromResult<MediaItem?>(ready.Item), // Ready slots stand in as completed (SPEC F191.2)
        SlotSource => throw new UnreachableException($"Unhandled {nameof(SlotSource)} case: {slot.Source.GetType()}"),
    };

    /// <summary>
    /// SPEC F191.4's verbatim law: <see cref="VerbatimSource.AllowFlavor"/> true tries the
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
            ? await ResolveFlavoredCopyAsync(verbatim.Request, verbatim.Copy.Text, ct)
            : null;

        var copy = flavoredText is { } text ? verbatim.Copy with { Text = text } : verbatim.Copy;
        var rendered = await renderer.RenderAsync(verbatim.Request, copy, ct);

        return rendered is { } item
            ? item with { MediaId = AnnouncementMediaId.Wrap(AnnouncementIdOf(slot), item.MediaId) }
            : null;
    }

    /// <summary>
    /// SPEC F144.4's fallback law, moved here quiet (SPEC F191.5 — this class never logs): any
    /// failure of <see cref="IAnnouncementCopyWriter"/>, including a throw, resolves to
    /// <see langword="null"/> so the caller falls back to the plain copy silently. The fault stays
    /// observable without a log here because <see cref="IAnnouncementCopyWriter"/> contracts never to
    /// throw except on the caller's own <paramref name="ct"/>, and the one production implementation
    /// (<c>LlmCopyWriter</c>) already warns on every fault path with F144.4's own fallback wording —
    /// so the catch below guards a contract breach, not a routine path.
    /// </summary>
    async Task<string?> ResolveFlavoredCopyAsync(SegmentRequest announcementRequest, string message, CancellationToken ct)
    {
        if (announcementCopyWriter is not { } writer) return null;

        try
        {
            return await writer.WriteAnnouncementAsync(announcementRequest, message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static long AnnouncementIdOf(PlannedSlot slot) =>
        slot.Reservation is { Kind: ReservationKind.Announcement, Key: var key }
            ? long.Parse(key, CultureInfo.InvariantCulture)
            : throw new UnreachableException("Announcement slot without an Announcement reservation (SPEC F187.3)");
}
