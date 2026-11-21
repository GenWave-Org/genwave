namespace GenWave.Host.Api;

/// <summary>
/// <c>GET /api/voices/engines</c> body (SPEC F219.2): every engine in the registry and the one
/// <c>Tts:Engine</c> currently names. Endpoints are deliberately absent — the browser never needs them.
/// </summary>
public sealed record VoiceEnginesResponse(string Active, IReadOnlyList<VoiceEngineSummary> Engines);
