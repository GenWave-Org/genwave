namespace GenWave.Tts;

using System.Text.Json.Serialization;

/// <summary>One completion choice in a <see cref="ChatCompletionResponse"/> (SPEC F34.3 wire shape).</summary>
sealed record ChatCompletionChoice([property: JsonPropertyName("message")] ChatCompletionMessage? Message);
