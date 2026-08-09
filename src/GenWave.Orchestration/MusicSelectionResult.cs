namespace GenWave.Orchestration;

using GenWave.Core.Domain;

/// <summary>
/// SPEC F111.1/F112.3 (PLAN T234) — <see cref="MusicSelectionPolicy.SelectMusicCandidateAsync"/>'s
/// return shape: the picked candidate (<see langword="null"/> only on a genuine drain, SPEC F41.2)
/// alongside the <see cref="BoundaryOutcome"/> rung the ladder settled on for THIS pick. A policy
/// outcome, never Orchestrator state — <see cref="Orchestrator"/> reads <see cref="Outcome"/> back
/// off this result rather than re-deriving it from the <see cref="BoundaryFitPlan"/> a second time.
/// Internal planning state only, same as <see cref="BoundaryFitPlan"/> itself — never rides an item
/// or crosses the assembly boundary.
/// </summary>
internal sealed record MusicSelectionResult(RotationCandidate? Candidate, BoundaryOutcome Outcome);
