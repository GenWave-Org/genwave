namespace GenWave.MediaLibrary.Mood;

using System.Text.Json.Serialization;

/// <summary>One completion choice in a <see cref="MoodChatCompletionResponse"/>.</summary>
sealed record MoodChatCompletionChoice(
    [property: JsonPropertyName("message")] MoodChatCompletionMessage? Message);
