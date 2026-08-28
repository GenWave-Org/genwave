namespace GenWave.Core.Llm;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Serializer options every <c>/v1/chat/completions</c> request body in this solution is written
/// with (gh-#620): identical to <c>JsonContent.Create</c>'s own web defaults except that a
/// <see langword="null"/> member is LEFT OUT of the JSON rather than written as <c>null</c>. That is
/// what lets <see cref="ReasoningEffort.ToWire"/>'s null mean "do not send <c>reasoning_effort</c>"
/// — <see cref="ReasoningEffort.Omit"/> must reproduce the pre-gh-#620 request byte for byte for a
/// backend that rejects the field, and <c>"reasoning_effort": null</c> is not that.
/// </summary>
public static class ChatCompletionRequestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
