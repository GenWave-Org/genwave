namespace GenWave.Orchestration;

/// <summary>
/// gh-#254 — one boundary fit, computed by <c>Orchestrator.BuildBoundaryFit</c> per in-window music
/// pick: the effective track length (post-crossfade-trim) the sampler should aim for, and how far a
/// sample may miss it while still counting as a win. Internal planning state only — never rides an
/// item or crosses a seam.
/// </summary>
/// <param name="DesiredEffectiveLength">
/// The candidate length that lands its end (and the break patter that follows) exactly on the
/// boundary, after subtracting queued-ahead drift and this unit's own pre-music patter. Can be
/// negative when the approach has already overshot — the sampler then prefers the least-late pick.
/// </param>
/// <param name="Tolerance">
/// The win window (gh-#254: ±30s base, widened as the worst contributing gh-#253 estimate's
/// confidence tier drops). The first sample landing inside it is kept as-is — the degenerate-pick
/// guard.
/// </param>
sealed record BoundaryFitPlan(TimeSpan DesiredEffectiveLength, TimeSpan Tolerance);
