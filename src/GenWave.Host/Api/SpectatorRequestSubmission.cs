namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /spectator/api/requests</c> (SPEC F87.1, STORY-224, PLAN T87). Exactly
/// one field — nothing else is bindable, so there is no mass-assignment surface here (no id, no
/// status, no expiry the caller could set).
/// </summary>
/// <param name="Wish">
/// The listener's free-text request. <see langword="null"/>, empty, whitespace-only, or longer
/// than <c>Requests:WishMaxLength</c> characters all fail the same way: 400, nothing written
/// (SPEC F87.1).
/// </param>
public sealed record SpectatorRequestSubmission(string? Wish);
