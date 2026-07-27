namespace GenWave.MediaLibrary.ExplicitClassification;

using System.Text.Json.Serialization;

/// <summary>
/// Wire shape of an OpenAI-compatible <c>POST /v1/chat/completions</c> response — only the fields
/// <see cref="OllamaExplicitClassifier"/> needs. MediaLibrary's own copy, deliberately not shared
/// with <c>GenWave.Tts.ChatCompletionResponse</c> (that type is <c>internal</c> to a project this
/// one must never reference) nor with <c>GenWave.MediaLibrary.Mood.MoodChatCompletionResponse</c>
/// (a separate feature's own small duplicate of the same stable, public OpenAI convention) — three
/// record shapes is cheaper and more honest than either cross-module or cross-feature coupling.
/// </summary>
sealed record ExplicitChatCompletionResponse(
    [property: JsonPropertyName("choices")] List<ExplicitChatCompletionChoice>? Choices);
