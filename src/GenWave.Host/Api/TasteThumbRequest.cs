namespace GenWave.Host.Api;

/// <summary>
/// Body of <c>POST /api/booth-log/{id}/taste-thumb</c> (SPEC F84.1, STORY-215, PLAN T70).
/// <see cref="Direction"/> is case-insensitive <c>"up"</c> or <c>"down"</c> — mirrors
/// <see cref="VoteRequest.Direction"/>'s own shape (the two are structurally identical by
/// coincidence, never by a shared type: F84.7 keeps persona taste and media rating disjoint).
/// </summary>
public sealed record TasteThumbRequest(string Direction);
