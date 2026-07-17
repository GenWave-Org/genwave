namespace GenWave.Core.Domain;

/// <summary>
/// Outcome of <see cref="Abstractions.IMediaRating.VoteAsync"/> (SPEC F33.3). <see cref="Score"/> is
/// populated only when <see cref="Result"/> is <see cref="RatingWriteResult.Updated"/> — the
/// post-vote value, already clamped to <c>[0,100]</c> — so a caller can never mistake a
/// <see cref="RatingWriteResult.NotFound"/> outcome for a real score.
/// </summary>
public readonly record struct VoteOutcome(RatingWriteResult Result, int? Score);
