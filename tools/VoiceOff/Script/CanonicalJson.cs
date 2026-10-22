using System.Text.Json.Nodes;

namespace GenWave.VoiceOff.Script;

/// <summary>Canonical JSON: object keys sorted ordinally, no whitespace. The bytes a content hash is taken over.</summary>
internal static class CanonicalJson
{
    internal static string Of(JsonNode node) => Sort(node)?.ToJsonString() ?? "null";

    private static JsonNode? Sort(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                JsonObject sorted = [];
                foreach (KeyValuePair<string, JsonNode?> pair in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    sorted[pair.Key] = Sort(pair.Value);
                }

                return sorted;
            case JsonArray array:
                JsonArray copy = [];
                foreach (JsonNode? item in array)
                {
                    copy.Add(Sort(item));
                }

                return copy;
            default:
                return node?.DeepClone();
        }
    }
}
