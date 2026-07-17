namespace GenWave.Tts;

using System.Text.Json.Serialization;

/// <summary>The message payload of a <see cref="ChatCompletionChoice"/> (SPEC F34.3 wire shape).</summary>
sealed record ChatCompletionMessage([property: JsonPropertyName("content")] string? Content);
