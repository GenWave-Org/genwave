namespace GenWave.Tts;

using System.Text.Json.Serialization;

/// <summary>
/// One completion choice in a <see cref="ChatCompletionResponse"/> (SPEC F34.3 wire shape).
/// <see cref="FinishReason"/> (SPEC F127.4, F127.11, PLAN T282 — the OpenAI/ollama-compatible
/// <c>finish_reason</c> field, e.g. <c>"stop"</c>/<c>"length"</c>) is read only by
/// <see cref="CrosstalkScriptWriter"/>, which discards the whole exchange when it is
/// <c>"length"</c> — a completion cut short by <c>max_tokens</c> leaves a truncated last line that
/// can still PARSE cleanly and would otherwise air mid-word (the gh-#424 class, one seam over).
/// <see langword="null"/> when the wire omits it (an endpoint that predates this field). Purely
/// additive: <see cref="LlmCopyWriter"/> never reads this property, so its own byte-identical
/// deserialization/behavior is unaffected by this field's addition.
/// </summary>
sealed record ChatCompletionChoice(
    [property: JsonPropertyName("message")] ChatCompletionMessage? Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason = null);
