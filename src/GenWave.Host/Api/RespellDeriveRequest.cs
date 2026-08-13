namespace GenWave.Host.Api;

/// <summary>Request body for <c>POST /api/pronunciations/derive</c> (SPEC F126.2, STORY-324).</summary>
public sealed record RespellDeriveRequest(string? Respelling);
