namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/media/bulk/never-play</c> (SPEC F61.1, F61.2, STORY-158).
///
/// <see cref="Filter"/> is required — a missing/null <c>filter</c> is rejected with 400 before
/// anything is written, mirroring <see cref="BulkVoteRequest"/>'s rule.
///
/// <see cref="NeverPlay"/> idempotently sets the flag on every matched row; a later sweep with the
/// opposite value restores them — never a one-way door (F33.4, F61.2).
/// </summary>
public sealed record BulkNeverPlayRequest(BulkRatingFilter? Filter, bool NeverPlay);
