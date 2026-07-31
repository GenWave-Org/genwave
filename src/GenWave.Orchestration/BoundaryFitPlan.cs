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
    PatterEstimateConfidence Confidence);
