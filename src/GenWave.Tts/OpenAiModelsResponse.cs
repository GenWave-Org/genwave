namespace GenWave.Tts;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Wire shape of an OpenAI-compatible <c>GET /v1/models</c> response: <c>{ "data": [...] }</c>,
/// where each entry is normally an <c>{ "id": ..., "object": "model", ... }</c> object but is held
/// as a raw <see cref="JsonElement"/> (mirroring <see cref="KokoroVoicesResponse"/>'s own tolerance)
/// so a bare id string still deserializes — <c>Llm:Endpoint</c> is operator-repointable at runtime
/// (F36.4), so the wire shape on the other end is not fixed at build time. Internal — callers only
/// ever see the flattened id list via <see cref="Core.Abstractions.ILlmModelLister"/>.
/// </summary>
sealed record OpenAiModelsResponse([property: JsonPropertyName("data")] List<JsonElement>? Data)
{
    public IReadOnlyList<string> ModelIds()
    {
        if (Data is null)
            return [];

        var ids = new List<string>(Data.Count);
        foreach (var entry in Data)
        {
            var id = entry.ValueKind switch
            {
                JsonValueKind.String => entry.GetString(),
                JsonValueKind.Object when entry.TryGetProperty("id", out var idProperty)
                                          && idProperty.ValueKind == JsonValueKind.String
                    => idProperty.GetString(),
                _ => null,
            };

            if (!string.IsNullOrEmpty(id))
                ids.Add(id);
        }

        return ids;
    }
}
