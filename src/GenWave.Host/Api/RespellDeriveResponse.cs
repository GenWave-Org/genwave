namespace GenWave.Host.Api;

/// <summary>
/// Response body for <c>POST /api/pronunciations/derive</c> on success (SPEC F126.2, STORY-324):
/// candidate IPA, returned RAW for the operator to audition — this endpoint never persists it.
/// </summary>
public sealed record RespellDeriveResponse(string Ipa);
