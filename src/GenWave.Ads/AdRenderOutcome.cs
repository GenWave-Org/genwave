namespace GenWave.Ads;

/// <summary>
/// What <see cref="AdRenderService.RenderAsync"/> actually did to the spot's own row (PLAN T402, T401
/// review F1) — narrower than a full result type on purpose: every render already reports its
/// human-readable detail through the row's own <c>fail_reason</c> (via <see cref="AdSpot"/> once
/// re-read) or the confirmed <c>media_id</c>; this enum exists ONLY to let <c>AdSpotWorker</c> tell a
/// genuine transition apart from a claim CONFLICT it must never retry blindly.
/// </summary>
public enum AdRenderOutcome
{
    /// <summary>The spot reached <c>ready</c> — <see cref="GenWave.Core.Abstractions.IAdSpotStore.MarkReadyAsync"/>
    /// applied.</summary>
    Rendered,

    /// <summary>The spot reached <c>failed</c> — <see cref="GenWave.Core.Abstractions.IAdSpotStore.MarkFailedAsync"/>
    /// applied, stamping a <c>fail_reason</c> an operator (or a later automatic retry) can act on.</summary>
    Failed,

    /// <summary>
    /// Neither <c>MarkReadyAsync</c> NOR the follow-up <c>MarkFailedAsync</c> applied — the row was no
    /// longer <c>rendering</c> by the time either write ran (PLAN T402 review block 1: the
    /// stuck-rendering guardian re-armed this SAME spot back to <c>approved</c> mid-render, most
    /// likely because <c>AdSpotLifecycleGuardianService</c>'s own grace and <c>AdSpotWorker</c>'s own
    /// render budget drifted out of the relation that is supposed to make this structurally rare —
    /// see that guardian's own remarks). <c>AdSpotWorker</c> logs this outcome and stops for the
    /// tick rather than attempting any recovery of its own: the row is ALREADY back in <c>approved</c>
    /// (the guardian's own write), so the very next claim naturally retries it — nothing here needs
    /// fixing beyond the log line that lets an operator notice the relation drifted.
    /// </summary>
    ClaimConflict,
}
