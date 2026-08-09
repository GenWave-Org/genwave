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
/// <param name="Candidate">The picked candidate, or <see langword="null"/> only on a genuine drain (SPEC F41.2).</param>
/// <param name="Outcome">Which rung of the SPEC F111.1 ladder this pick resolved to.</param>
/// <param name="CrossesBoundary">
/// T235 review findings F1/F5 — true only when <see cref="Candidate"/> carries a measured duration
/// AND its effective length (post-crossfade-trim) reaches or exceeds
/// <see cref="BoundaryFitPlan.UntilBoundary"/>: this pick will genuinely still be airing when the
/// boundary itself arrives. Computed HERE, in <see cref="MusicSelectionPolicy"/>, never re-derived by
/// <see cref="Orchestrator"/> — this class is the only component that ever sees the candidate's own
/// effective length alongside the fit it was measured against (F5: "fix at the source"). A
/// duration-less pick NEVER claims crossing — there is no measured length to compare against the
/// boundary at all — and every <see cref="BoundaryOutcome.None"/>/<see cref="BoundaryOutcome.Fit"/>
/// result reports <see langword="false"/> unconditionally: the fact has exactly one consumer
/// (<c>Orchestrator.GetNextAsync</c>'s straddle branch), and that branch only ever reads it when
/// <see cref="Outcome"/> is <see cref="BoundaryOutcome.Straddle"/>.
///
/// <para>
/// Corrects the pre-T235 shape (F1), where <see cref="BoundaryOutcome.Straddle"/> alone forced a
/// pending SignOff ahead of ANY off-tolerance pick clearing <see cref="MusicSelectionPolicy.MusicFloor"/>
/// — including a track far SHORTER than the desired room, which cannot possibly cross the boundary.
/// That forced the sign-off 4-6 minutes early, with several more tracks still due to play before the
/// real boundary, and left the sign-on lying about "still playing when you took the chair."
/// </para>
/// </param>
internal sealed record MusicSelectionResult(RotationCandidate? Candidate, BoundaryOutcome Outcome, bool CrossesBoundary);
