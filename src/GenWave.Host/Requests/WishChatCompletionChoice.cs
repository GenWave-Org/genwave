namespace GenWave.Host.Requests;

using System.Text.Json.Serialization;

/// <summary>One completion choice in a <see cref="WishChatCompletionResponse"/>.</summary>
sealed record WishChatCompletionChoice(
    [property: JsonPropertyName("message")] WishChatCompletionMessage? Message);
