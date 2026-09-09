namespace GenWave.Core.Domain;

/// <summary>
/// Pairs <see cref="AdSpotJobStampResult"/> with the row's fresh state after
/// <c>Abstractions.IAdSpotStore.StampJobAsync</c> (SPEC F174, F175; STORY-406; PLAN T432) — mirrors
/// <see cref="AdSpotTransitionOutcome"/>'s own pairing one seam over.
/// </summary>
/// <param name="Result">The stamp attempt's outcome.</param>
/// <param name="Spot">The row's fresh state — populated only when <see cref="Result"/> is
/// <see cref="AdSpotJobStampResult.Stamped"/>; every other outcome carries <see langword="null"/>.</param>
public readonly record struct AdSpotJobStampOutcome(AdSpotJobStampResult Result, AdSpot? Spot);
