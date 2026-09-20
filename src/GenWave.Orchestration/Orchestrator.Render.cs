namespace GenWave.Orchestration;

// PLAN T536 — the render phase's only remaining Orchestrator-side job: hand a BreakPlan built by
// BreakPlanner (SPEC F188) to BreakRenderer (SPEC F191), then hand the resulting per-slot outcomes to
// BreakDelivery (SPEC F192), which stamps, reports drops, and settles every reservation. Split from
// Orchestrator.cs purely for the ~300-line budget (csharp-best-practices) — shares that file's
// ctor-level fields via the partial class.
//
// SPEC F192.5's cancellation law lives HERE, not on BreakDelivery.Deliver itself (that method's own
// signature, SPEC F192.1, takes no CancellationToken) — the Orchestrator is the one class that knows
// "before delivery" means "before this call", so it is the one class that can observe ct firing before
// that instant. The check sits AFTER the render await, not before it (round-2 review finding F1):
// BreakRenderer.ResolveOutcomeAsync never throws on cancellation, and a ReadySource slot (Ad,
// Crosstalk, a pooled StationId) is always a Task.FromResult — already complete at kick time — so a
// pre-render check alone lets that slot's RenderedOutcome survive into the buffer regardless of when
// ct fired. Checking between render and delivery is the only placement that actually keeps a
// mid-render cancellation from partially enqueueing. PlanAndRenderAsync (Orchestrator.cs) stays
// untouched: this is the ONE call both GetNextAsync and TryServeCeremonyOnlyUnitAsync already funnel
// through, so the check belongs at this choke point, not duplicated at each caller.
public sealed partial class Orchestrator
{
    /// <summary>
    /// Renders <paramref name="plan"/> (SPEC F191) and delivers the result (SPEC F192): a
    /// <paramref name="ct"/> that has fired by the time render completes discards the whole unit —
    /// no survivor reaches <see cref="buffer"/> (SPEC F192.5's buffer-side guarantee) — without
    /// calling <see cref="BreakDelivery.Deliver"/>; otherwise every survivor in
    /// <see cref="BreakOutcome.Items"/> is enqueued in the SAME ordinal order
    /// <see cref="BreakDelivery.Deliver"/> returned it (SPEC F192.6 — no other class touches the
    /// buffer).
    /// </summary>
    async Task RenderPlanAsync(BreakPlan plan, CancellationToken ct)
    {
        var outcomes = await breakRenderer.RenderAsync(plan, ct);

        if (ct.IsCancellationRequested)
            return;

        var outcome = breakDelivery.Deliver(plan, outcomes);

        foreach (var item in outcome.Items)
            buffer.Enqueue(item);
    }
}
