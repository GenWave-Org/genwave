namespace GenWave.Host.Api;

/// <summary>Response body for <c>POST /api/tts/normalize-preview</c> on success (SPEC F68.6).</summary>
public sealed record NormalizePreviewResponse(string Spoken);
