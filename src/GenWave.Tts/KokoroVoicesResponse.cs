namespace GenWave.Tts;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Wire shape of Kokoro's <c>GET /v1/audio/voices</c> response: <c>{ "voices": [...] }</c>, where
/// each entry is a bare id string (kokoro-fastapi ≤ v0.2.x) or an <c>{ "id": ..., "name": ... }</c>
/// object (v0.6.0, confirmed against <c>ghcr.io/remsky/kokoro-fastapi-cpu:v0.6.0</c>). Entries are
/// held as raw <see cref="JsonElement"/>s so one record deserializes both generations —
/// <c>Tts:Endpoint</c> is operator-repointable at runtime (F36.4), so the wire shape on the other
/// end is not fixed at build time. Internal — callers only ever see the flattened id list via
/// <see cref="GenWave.Core.Abstractions.ITtsVoiceLister"/>.
/// </summary>
sealed record KokoroVoicesResponse([property: JsonPropertyName("voices")] List<JsonElement>? Voices)
{
    public IReadOnlyList<string> VoiceIds()
    {
        if (Voices is null)
            return [];

        var ids = new List<string>(Voices.Count);
        foreach (var entry in Voices)
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
