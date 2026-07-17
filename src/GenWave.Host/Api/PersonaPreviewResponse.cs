namespace GenWave.Host.Api;

/// <summary>Response body for <c>POST /api/personas/preview</c> on success (SPEC F35.6).</summary>
public sealed record PersonaPreviewResponse(string Text);
