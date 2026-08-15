namespace GenWave.Tts;

using System.Text.Json.Serialization;

/// <summary>
/// Wire shape of an OpenAI-compatible <c>POST /v1/chat/completions</c> response (SPEC F34.3),
/// shared by <see cref="LlmCopyWriter"/> and (as of PLAN T282, SPEC F127.3) <see cref="CrosstalkScriptWriter"/>
/// — only the fields either one needs, see <see cref="ChatCompletionChoice.FinishReason"/>'s own
/// remarks for exactly which class reads which field. Internal — callers only ever see the
/// extracted, cleaned copy via <see cref="GenWave.Core.Abstractions.ISegmentCopyWriter"/> (or, for
/// crosstalk, a validated <see cref="CrosstalkScript"/>).
/// </summary>
sealed record ChatCompletionResponse(
    [property: JsonPropertyName("choices")] List<ChatCompletionChoice>? Choices);
