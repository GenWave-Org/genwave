namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/media/bulk/vote</c> (SPEC F61.1, STORY-158).
///
/// <see cref="Filter"/> is required — a missing/null <c>filter</c> is rejected with 400 before
/// anything is written (<see cref="BulkRatingController"/>'s "malformed filter" sad path); an
/// empty filter object (<c>{}</c>) is valid and, bounded by the effective scope, sweeps every
/// in-scope row.
///
/// <see cref="Direction"/> is matched case-insensitively against <c>"up"</c>/<c>"down"</c> by
/// <see cref="BulkRatingController"/> — the same rule <see cref="RatingController"/> applies to
/// the per-row vote (F33.3); any other value is rejected with 400 before anything is written.
/// </summary>
public sealed record BulkVoteRequest(BulkRatingFilter? Filter, string Direction);
