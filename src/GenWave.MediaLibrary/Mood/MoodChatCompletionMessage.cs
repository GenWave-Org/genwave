namespace GenWave.MediaLibrary.Mood;

using System.Text.Json.Serialization;

/// <summary>The message payload of a <see cref="MoodChatCompletionChoice"/>.</summary>
sealed record MoodChatCompletionMessage(
    [property: JsonPropertyName("content")] string? Content);
