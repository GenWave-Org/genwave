namespace GenWave.Core.Domain;

/// <summary>
/// One answer from <see cref="Abstractions.IPatterDurationEstimator"/> (gh-#253): how long an
/// upcoming, not-yet-rendered patter segment is expected to run, plus which honesty tier the
/// number came from. Purely advisory — measured duration (SPEC F66.1) remains the only value ever
/// stamped onto an airing item; this type never leaves the planning path.
/// </summary>
/// <param name="Duration">The expected spoken length of the segment.</param>
/// <param name="Confidence">Which tier produced <paramref name="Duration"/> — see each member's remarks.</param>
public sealed record PatterDurationEstimate(TimeSpan Duration, PatterEstimateConfidence Confidence);
