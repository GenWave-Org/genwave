namespace GenWave.Orchestration;

/// <summary>
/// SPEC F111.1 (gh-#320, PLAN T234) — the boundary-fit ladder's three named rungs, decided by
/// <see cref="MusicSelectionPolicy.SelectMusicCandidateAsync"/> (F112.3: a policy outcome, never
/// Orchestrator state — <see cref="Orchestrator"/> reads this back rather than re-deriving it), plus
/// <see cref="None"/> for every pick the ladder does not govern at all.
/// </summary>
internal enum BoundaryOutcome
{
    /// <summary>
    /// No <see cref="BoundaryFitPlan"/> was in play for this pick — outside the F74.3 lookahead
    /// window (the everyday no-imminent-boundary case), or the SPEC F87.6 rung-(-1) request
    /// fulfillment short-circuit, which returns before the fit is even consulted.
    /// </summary>
    None,

    /// <summary>
    /// A sampled candidate landed within <see cref="BoundaryFitPlan.Tolerance"/> — the shipped
    /// gh-#254 win, byte-identical.
    /// </summary>
    Fit,

    /// <summary>
    /// Nothing sampled landed within tolerance, but <see cref="BoundaryFitPlan.DesiredEffectiveLength"/>
    /// stayed at-or-above <see cref="MusicSelectionPolicy.MusicFloor"/>: the ladder's middle rung
    /// (gh-#320) — a deliberate least-late/best-crossing pick, reported so T235's unit assembly can
    /// ceremony around it (sign-off ahead of the crossing track, sign-on held to the far seam).
    /// </summary>
    Straddle,

    /// <summary>
    /// <see cref="BoundaryFitPlan.DesiredEffectiveLength"/> fell below
    /// <see cref="MusicSelectionPolicy.MusicFloor"/> — the shipped gh-#300 rung, now truly
    /// last-resort: no music unit belongs in front of the boundary at all.
    /// </summary>
    CeremonyOnly,
}
