namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>PUT /api/media/{id}/never-play</c> (SPEC F33.4).
/// </summary>
public sealed record NeverPlayRequest(bool NeverPlay);
