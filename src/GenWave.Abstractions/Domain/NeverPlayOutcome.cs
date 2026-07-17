namespace GenWave.Core.Domain;

/// <summary>
/// Outcome of <see cref="Abstractions.IMediaRating.SetNeverPlayAsync"/> (SPEC F33.4).
/// <see cref="NeverPlay"/> is populated only when <see cref="Result"/> is
/// <see cref="RatingWriteResult.Updated"/> — the flag's post-write value, which for this idempotent
/// set always echoes back what the caller asked for.
/// </summary>
public readonly record struct NeverPlayOutcome(RatingWriteResult Result, bool? NeverPlay);
