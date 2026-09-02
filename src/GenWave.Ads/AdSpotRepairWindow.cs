namespace GenWave.Ads;

/// <summary>
/// <see cref="AdSpotWorker"/>'s own repair-sweep recency window (PLAN T402 review F6) —
/// DELIBERATELY WIDER than <see cref="AdSpotGuardianGrace"/>'s own grace, and computed by composing
/// it rather than reading it directly.
///
/// <para>
/// <b>Why grace alone left the window unreachable at production defaults.</b>
/// <see cref="AdSpotWorker.RepairReadyEligibilityAsync"/> is not a continuously running sweep — it is
/// STEP ONE of <see cref="AdSpotWorker"/>'s own tick, and a spot cannot become
/// <see cref="AdState.Ready"/> before STEP FOUR (the render pass) of that SAME tick at the absolute
/// earliest. So the EARLIEST this sweep can ever look at a spot that just went Ready is the NEXT
/// tick — roughly one whole <c>Ads:WorkerIntervalMinutes</c> later (10 minutes, the production
/// default). Sizing the repair window on <see cref="AdSpotGuardianGrace"/>'s own grace ALONE (5
/// minutes at production defaults: <c>RenderBudgetSeconds</c> 180s + <see cref="AdSpotGuardianGrace.Margin"/>
/// 2min) meant a fresh orphan had ALREADY aged past that 5-minute window by the time this sweep was
/// next even given the chance to look at it — the window was mathematically unreachable, not merely
/// tight, at the shipped defaults.
/// </para>
///
/// <para>
/// <b>The fix: add the observation cadence itself as headroom.</b> <see cref="Compute"/> adds
/// <c>Ads:WorkerIntervalMinutes</c> — the worst-case gap before this sweep gets its first look at a
/// row — on TOP of <see cref="AdSpotGuardianGrace.Compute"/>'s own safety margin, so an orphan born
/// during tick N is provably still inside the window when tick N+1's own repair step runs. This is
/// NOT the guardian's own grace widened (that stays exactly as sized — a stuck-rendering row's own
/// re-arm timing is a different question this class never touches); it is a SEPARATE, wider window
/// this worker's own repair sweep alone reads, composed from the guardian's grace plus this sweep's
/// own cadence.
/// </para>
///
/// <para>
/// <b>The operator-intent line moves out to match (PLAN T402 review F6's own steer — widen, never go
/// unconditional).</b> A Ready spot's media row is worker-owned for roughly one tick interval PLUS
/// the guardian's grace after its own ready transition — inside that (now correctly reachable)
/// window, an ineligible row can only be the exact race
/// <see cref="AdSpotWorker.RepairReadyEligibilityAsync"/>'s own remarks describe; OUTSIDE it, an
/// ineligible Ready row is still, and forever, an operator's own hand (<c>never_play</c>) —
/// <see cref="AdSpotWorker.RepairReadyEligibilityAsync"/> never touches it, at any age past this
/// window, no matter how far past.
/// </para>
/// </summary>
internal static class AdSpotRepairWindow
{
    /// <summary>The live window: <c>Ads:WorkerIntervalMinutes</c> (re-read every call — never cached,
    /// so a live edit to either knob is honored on the very next tick) plus
    /// <see cref="AdSpotGuardianGrace.Compute"/>.</summary>
    internal static TimeSpan Compute(AdsOptions options) =>
        TimeSpan.FromMinutes(options.WorkerIntervalMinutes) + AdSpotGuardianGrace.Compute(options);
}
