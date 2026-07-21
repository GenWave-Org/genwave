namespace GenWave.Host.Api;

/// <summary>Request body for <c>POST /api/tts/normalize-preview</c> (SPEC F68.6).</summary>
public sealed record NormalizePreviewRequest(string? Text);
