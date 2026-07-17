namespace GenWave.Tts;

using System.Text.Json.Serialization;

/// <summary>
/// Wire shape of Kokoro's <c>GET /v1/audio/voices</c> response: <c>{ "voices": [...] }</c>
/// (confirmed against <c>ghcr.io/remsky/kokoro-fastapi-cpu:v0.2.1</c>, R2). Internal — callers only
/// ever see the flattened id list via <see cref="GenWave.Core.Abstractions.ITtsVoiceLister"/>.
/// </summary>
sealed record KokoroVoicesResponse([property: JsonPropertyName("voices")] List<string>? Voices);
