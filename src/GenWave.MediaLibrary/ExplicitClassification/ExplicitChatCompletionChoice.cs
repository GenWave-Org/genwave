namespace GenWave.MediaLibrary.ExplicitClassification;

using System.Text.Json.Serialization;

/// <summary>One completion choice in an <see cref="ExplicitChatCompletionResponse"/>.</summary>
sealed record ExplicitChatCompletionChoice(
    [property: JsonPropertyName("message")] ExplicitChatCompletionMessage? Message);
