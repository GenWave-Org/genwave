namespace GenWave.MediaLibrary.Mood;

using System.Text.Json.Serialization;

/// <summary>
/// Wire shape of an OpenAI-compatible <c>POST /v1/chat/completions</c> response — only the fields
/// <see cref="OllamaMoodTagger"/> needs. MediaLibrary's own copy, deliberately not shared with
/// <c>GenWave.Tts.ChatCompletionResponse</c> (that type is <c>internal</c> to a project this one must
/// never reference); the wire shape itself is a stable, public OpenAI convention, so this small
/// duplication is cheaper and more honest than a cross-module dependency for three record shapes.
/// </summary>
sealed record MoodChatCompletionResponse(
    [property: JsonPropertyName("choices")] List<MoodChatCompletionChoice>? Choices);
