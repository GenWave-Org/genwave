namespace GenWave.Orchestration;

/// <summary>
/// SPEC F111.5 (T218 review finding F2, PLAN T234) — the boundary-fit log line's own seam, as a
/// named one-method interface rather than the
/// <c>Action&lt;BoundaryFitPlan, string, IReadOnlyList&lt;TimeSpan&gt;, TimeSpan?&gt;</c> delegate
/// <see cref="MusicSelectionPolicy.SelectMusicCandidateAsync"/> used to thread through: every call
/// site can now name its own arguments (the delegate's un-nameable trailing <c>null</c> the review
/// flagged — <c>logBoundaryFit(fit, "drained", sampled, null)</c> could never write
/// <c>chosenDiff: null</c> — dies with it, since a real method's own parameter names are always
/// nameable at the call site).
///
/// <para>
/// <see cref="Orchestrator"/> implements this explicitly (see its own remarks) so every boundary-fit
/// line still lands on the SAME <c>ILogger&lt;Orchestrator&gt;</c> sink
/// <c>TryServeCeremonyOnlyUnitAsync</c>'s own "declined" line uses — one boundary-fit log, one owner,
/// regardless of which class decided the outcome. <see cref="NoOpBoundaryFitLog"/> is the default
/// binding, mirroring every other optional seam this policy threads (<c>IEnvelopeProvider</c>,
/// <c>IPersonaPickProvider</c>, <c>IRequestFulfillmentSource</c>).
/// </para>
/// </summary>
internal interface IBoundaryFitLog
{
    /// <summary>
    /// Records one boundary-fit evaluation (SPEC F111.5 — rung chosen, desired effective length via
    /// <paramref name="fit"/>, candidate found via <paramref name="chosenDiff"/>/<paramref name="sampled"/>).
    /// </summary>
    /// <param name="fit">The plan this evaluation reasoned from.</param>
    /// <param name="outcome">
    /// The pre-existing, finer-grained per-sample descriptor the gh-#254/gh-#300 log line vocabulary
    /// already spoke — "win"/"least-late"/"unscored"/"drained"/"declined (floor=…s)" — kept verbatim
    /// (additive, SPEC F111.5) so grep/Loki queries built against it survive unchanged.
    /// </param>
    /// <param name="rung">The SPEC F111.1 ladder rung <paramref name="fit"/> resolved to (gh-#320, PLAN T234).</param>
    /// <param name="sampled">Every effective length sampled this pick, in draw order.</param>
    /// <param name="chosenDiff">The winning candidate's diff from the desired length, when one was scored.</param>
    void Log(
        BoundaryFitPlan fit, string outcome, BoundaryOutcome rung, IReadOnlyList<TimeSpan> sampled,
        TimeSpan? chosenDiff);
}
