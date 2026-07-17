namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/tts/preview</c> (SPEC F35.6). <see cref="Voice"/> defaults to
/// <c>Station:Voice</c> when omitted — the Admin UI is expected to pass an explicit voice most of
/// the time, but a bare-text preview still works.
/// </summary>
public sealed record TtsPreviewRequest(string? Text, string? Voice);
