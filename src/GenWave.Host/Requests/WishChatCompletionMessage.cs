namespace GenWave.Host.Requests;

using System.Text.Json.Serialization;

/// <summary>The message payload of a <see cref="WishChatCompletionChoice"/>.</summary>
sealed record WishChatCompletionMessage(
    [property: JsonPropertyName("content")] string? Content);
