namespace GenWave.Core.Domain;

/// <summary>
/// Pairs <see cref="AdSpotWriteResult"/> with the row's fresh state after an xmin-guarded transition
/// (<c>Abstractions.IAdSpotStore</c>; SPEC F159.2; STORY-389; PLAN T398) — mirrors
/// <see cref="MediaUpdateOutcome"/>'s own pairing one table over, read straight from the
/// <c>UPDATE</c>'s own <c>RETURNING</c> clause, never a follow-up <c>SELECT</c>.
/// </summary>
/// <param name="Result">The transition's outcome.</param>
/// <param name="Spot">The row's fresh state, including its new <see cref="AdSpot.Version"/> —
/// populated only when <see cref="Result"/> is <see cref="AdSpotWriteResult.Updated"/>; every other
/// outcome carries <see langword="null"/> so a caller can never mistake a failed transition for a
/// fresh row.</param>
public readonly record struct AdSpotTransitionOutcome(AdSpotWriteResult Result, AdSpot? Spot);
