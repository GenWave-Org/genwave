namespace GenWave.Orchestration;

using GenWave.Core.Domain;

/// <summary>
/// gh-#254 — one boundary fit, computed by <c>Orchestrator.BuildBoundaryFit</c> per in-window music
/// pick: the effective track length (post-crossfade-trim) the sampler should aim for, and how far a
/// sample may miss it while still counting as a win. Internal planning state only — never rides an
/// item or crosses a seam.
///
/// <para>
/// gh-#300 carries the fit's own INPUTS alongside its two answers. They exist for one reason: the
/// 2:05 handoff could not be reconstructed from logs at all — only from render archaeology — because
/// nothing here was ever written down. See <c>Orchestrator.LogBoundaryFit</c> for the line they feed,
/// and note it is INFORMATION, not Debug: the demo fleet ships Information and above, so a Debug fit
/// line would have been exactly as invisible as no line at all.
/// </para>
/// </summary>
/// <param name="DesiredEffectiveLength">
/// The candidate length that lands its end (and the break patter that follows) exactly on the
/// boundary, after subtracting queued-ahead drift and this unit's own pre-music patter. Can be
/// negative when the approach has already overshot — the sampler then prefers the least-late pick,
/// unless gh-#300's floor says no music unit belongs here at all.
/// </param>
/// <param name="Tolerance">
/// The win window (gh-#254: ±30s base, widened as the worst contributing gh-#253 estimate's
/// confidence tier drops). The first sample landing inside it is kept as-is — the degenerate-pick
/// guard.
/// </param>
/// <param name="Kind">The pending deferral this fit aims at — a handoff reads very differently from an ident.</param>
/// <param name="UntilBoundary">Now to the boundary instant itself (for a SignOff, its due plus the lead time).</param>
/// <param name="QueuedAhead">The feeder's own measurement of runtime already committed ahead of this pass.</param>
/// <param name="PreMusicPatter">Estimated patter this unit plans between now and the candidate's first note.</param>
/// <param name="BreakPatter">Estimated patter between the candidate's last note and the boundary.</param>
/// <param name="Confidence">The WORST contributing gh-#253 estimate tier — what set <paramref name="Tolerance"/>.</param>
sealed record BoundaryFitPlan(
    TimeSpan DesiredEffectiveLength,
    TimeSpan Tolerance,
    SpeechDeferralKind Kind,
    TimeSpan UntilBoundary,
    TimeSpan QueuedAhead,
    TimeSpan PreMusicPatter,
    TimeSpan BreakPatter,
    PatterEstimateConfidence Confidence)
{
    /// <summary>
    /// SPEC F124.1 — true when the feeder's own already-queued runtime alone reaches or passes the
    /// boundary before this pick's candidate even starts: the crossing content IS that queued tail,
    /// not anything a music pick samples. <c>&gt;=</c>, not <c>&gt;</c> — exactly at the boundary
    /// still counts as crossing, the same edge convention <see cref="IsBelowFloor"/> uses for the
    /// floor.
    ///
    /// <para>
    /// Round-1 review finding F4 (PLAN T267): this was previously hand-duplicated at the one call site
    /// that needed it (<c>Orchestrator.TryServeCeremonyOnlyUnitAsync</c>'s own drain-instant clamp)
    /// with a DIFFERENT operator (<c>&gt;</c>) than this predicate's own <c>&gt;=</c> — harmless for a
    /// plain max (both operators agree at the tie), but two spellings of one comparison drift risk for
    /// no reason. <see cref="BoundaryFitPlan"/> owns both this predicate and
    /// <see cref="ClassifyOffToleranceRung"/> now (moved off <c>MusicSelectionPolicy</c>, which reached
    /// across to a sibling type's internals for a comparison that only ever needs this record's own
    /// fields) precisely so nothing outside this type ever hand-rolls the comparison again.
    /// </para>
    ///
    /// <para>
    /// <b>The null-estimate degrade (STORY-320 AC4) falls out for free, not as a special case.</b>
    /// <c>Orchestrator.BuildBoundaryFit</c> coalesces an unknown feeder estimate (<c>queuedAheadMs ??
    /// 0</c>) before it ever reaches <see cref="QueuedAhead"/> — this predicate never sees the null
    /// itself, only the coalesced zero. A <see cref="BoundaryFitPlan"/> is only ever built when
    /// <c>untilDue &gt; TimeSpan.Zero</c> (<c>Orchestrator.GetNextAsync</c>'s own guard), so
    /// <see cref="UntilBoundary"/> is always strictly positive for every fit this predicate is ever
    /// asked about — zero can never reach or pass a strictly positive boundary. An unknown estimate
    /// therefore ALWAYS takes the exact pre-F124 path: this predicate is false, and
    /// <see cref="IsBelowFloor"/> alone decides the rung, byte-identical to the shipped gh-#320 ladder.
    /// </para>
    /// </summary>
    internal bool QueuedTailCrossesBoundary => QueuedAhead >= UntilBoundary;

    /// <summary>
    /// True when <see cref="DesiredEffectiveLength"/> falls short of <paramref name="floor"/> — the
    /// ONE place that comparison is ever made (T234 review finding F3): both
    /// <see cref="ClassifyOffToleranceRung"/> below and <c>Orchestrator.ShouldDeclineFinalUnit</c> call
    /// this rather than each hand-writing what is supposed to be the same comparison as the other's
    /// complement. <c>&gt;=</c> is the floor's own edge convention (SPEC F112.3): exactly at the floor
    /// is NOT below it, so an exact-floor fit straddles rather than declines (pinned by
    /// <c>Story303_StraddleHandoff.DesiredExactlyAtFloorIsStraddleAndNotDeclined</c>).
    ///
    /// <para>
    /// <b>Crossing implies below-floor for a handoff kind — the ONE authoritative statement of this
    /// argument (round-1 review finding F8; every other call site below points back here rather than
    /// re-deriving it).</b> <see cref="QueuedTailCrossesBoundary"/> (<c>QueuedAhead &gt;= UntilBoundary</c>)
    /// forces <see cref="DesiredEffectiveLength"/> (<c>UntilBoundary - breakPatter - queuedAhead -
    /// preMusicPatter</c>) to zero or negative — a non-negative patter term subtracted from an already
    /// non-positive remainder — which can never clear a positive <paramref name="floor"/>. So a
    /// crossing fit is ALWAYS also below-floor: <c>Orchestrator.ShouldDeclineFinalUnit</c>, which reads
    /// this predicate ALONE (never <see cref="QueuedTailCrossesBoundary"/> directly), still intercepts
    /// every queue-crossing handoff fit without needing to widen its own condition — only
    /// <see cref="ClassifyOffToleranceRung"/>, consulted from inside the decline's own destination
    /// (<c>Orchestrator.TryServeCeremonyOnlyUnitAsync</c>), needed to learn about crossing at all.
    /// </para>
    /// </summary>
    internal bool IsBelowFloor(TimeSpan floor) => DesiredEffectiveLength < floor;

    /// <summary>
    /// SPEC F111.1/F124.1 — the ladder's rung once a pick has already missed tolerance (or, for the
    /// decline path, once <c>Orchestrator.ShouldDeclineFinalUnit</c> has already ruled this fit
    /// below-floor): <see cref="BoundaryOutcome.Straddle"/> when the queue itself already crosses the
    /// boundary (<see cref="QueuedTailCrossesBoundary"/>, checked FIRST — the crossing content there is
    /// the already-queued tail, not anything a pick samples) or when <see cref="DesiredEffectiveLength"/>
    /// still clears <paramref name="floor"/>; <see cref="BoundaryOutcome.CeremonyOnly"/> only when
    /// neither holds. Consulted from two independent call sites that must never drift apart —
    /// <c>MusicSelectionPolicy.SelectMusicCandidateAsync</c>'s own off-tolerance branch, and
    /// <c>Orchestrator.TryServeCeremonyOnlyUnitAsync</c>'s decline path (see that method's own remarks
    /// for why a Straddle verdict there can only mean the queue crosses) — never duplicated by hand at
    /// either one.
    /// </summary>
    internal BoundaryOutcome ClassifyOffToleranceRung(TimeSpan floor) =>
        QueuedTailCrossesBoundary || !IsBelowFloor(floor) ? BoundaryOutcome.Straddle : BoundaryOutcome.CeremonyOnly;
}
