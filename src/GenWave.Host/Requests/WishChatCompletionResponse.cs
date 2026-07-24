namespace GenWave.Host.Requests;

using System.Text.Json.Serialization;

/// <summary>
/// Wire shape of an OpenAI-compatible <c>POST /v1/chat/completions</c> response — only the fields
/// <see cref="LlmWishParser"/> needs. This project's own copy, deliberately not shared with
/// <c>GenWave.Tts.ChatCompletionResponse</c>/<c>GenWave.MediaLibrary.Mood.MoodChatCompletionResponse</c>
/// (both are <c>internal</c> to a project this one has no <c>InternalsVisibleTo</c> grant into); the
/// wire shape itself is a stable, public OpenAI convention, so this small duplication is cheaper and
/// more honest than reaching across a module boundary for three record shapes — the exact call
/// <c>MoodChatCompletionResponse</c>'s own remarks already made.
/// </summary>
sealed record WishChatCompletionResponse(
    [property: JsonPropertyName("choices")] List<WishChatCompletionChoice>? Choices);
