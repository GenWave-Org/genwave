namespace GenWave.MediaLibrary.ExplicitClassification;

using System.Text.Json.Serialization;

/// <summary>The message payload of an <see cref="ExplicitChatCompletionChoice"/>.</summary>
sealed record ExplicitChatCompletionMessage(
    [property: JsonPropertyName("content")] string? Content);
