namespace GenWave.Tts;

using System.Text.Json.Serialization;

/// <summary>The message payload of a <see cref="ChatCompletionChoice"/> (SPEC F34.3 wire shape).</summary>
/// <summary>
/// One reply message. <see cref="Reasoning"/> is the thinking-capable model's chain-of-thought as
/// Ollama's OpenAI-compatible layer serves it (gh-#620) — read for DIAGNOSIS only (a reasoning-only
/// reply is named as such in the fallback WARN), never aired, never treated as copy.
/// </summary>
sealed record ChatCompletionMessage(
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("reasoning")] string? Reasoning = null);
