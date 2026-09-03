namespace GenWave.Ads;

/// <summary>
/// The ONE grace computation both <see cref="AdSpotLifecycleGuardianService"/>'s own stuck-rendering
/// sweep AND (composed, not read directly — see <see cref="AdSpotRepairWindow"/>'s own remarks, PLAN
/// T402 review F6) <see cref="AdSpotWorker"/>'s own repair sweep are built on (PLAN T402 review
/// F1/F4) — a single time constant, not two independently-tuned copies that could drift apart and
/// reopen the exact race the guardian's own pinned relation exists to close (see
/// <see cref="AdSpotLifecycleGuardianService"/>'s own remarks for the full "grace exceeds the render
/// budget by construction" argument, which depends on every caller reading the SAME grace).
/// </summary>
internal static class AdSpotGuardianGrace
{
    /// <summary>
    /// Fixed headroom ADDED to the live <c>Ads:RenderBudgetSeconds</c> to compute the grace. Two
    /// minutes: comfortably longer than the worst-case gap between a render's own budget timer firing
    /// and its follow-up <c>MarkFailedAsync</c>/<c>ReArmAsync</c> write actually landing (a Postgres
    /// round trip, never itself budget-bounded), without leaving a genuinely crashed row — or a
    /// genuinely orphaned media row — unrepaired for materially longer than necessary.
    /// </summary>
    internal static readonly TimeSpan Margin = TimeSpan.FromMinutes(2);

    /// <summary>The live grace: <c>Ads:RenderBudgetSeconds</c> (re-read every call — never cached, so
    /// a live edit to that knob is honored on the very next sweep) plus <see cref="Margin"/>.</summary>
    internal static TimeSpan Compute(AdsOptions options) => TimeSpan.FromSeconds(options.RenderBudgetSeconds) + Margin;
}
