namespace GenWave.Tts;

using System.Text.Json.Serialization;

/// <summary>
/// Wire shape of an OpenAI-compatible <c>POST /v1/chat/completions</c> response (SPEC F34.3) —
/// only the fields <see cref="LlmCopyWriter"/> needs. Internal — callers only ever see the
/// extracted, cleaned copy via <see cref="GenWave.Core.Abstractions.ISegmentCopyWriter"/>.
/// </summary>
sealed record ChatCompletionResponse(
    [property: JsonPropertyName("choices")] List<ChatCompletionChoice>? Choices);
