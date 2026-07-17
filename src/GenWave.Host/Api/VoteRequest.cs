namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/media/{id}/vote</c> (SPEC F33.3). <see cref="Direction"/> is
/// matched case-insensitively against <c>"up"</c>/<c>"down"</c> by <see cref="RatingController"/>;
/// any other value is rejected with 400 before anything is written.
/// </summary>
public sealed record VoteRequest(string Direction);
