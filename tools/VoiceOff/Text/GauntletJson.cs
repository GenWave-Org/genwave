using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GenWave.VoiceOff.Text;

/// <summary>Reads a gauntlet file once, turning every JSON failure into an <see cref="InvalidDataException"/> that names the file.</summary>
internal static class GauntletJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowDuplicateProperties = false };

    /// <exception cref="InvalidDataException">The text is empty, not JSON, or the JSON null.</exception>
    internal static JsonNode Parse(string json, string fileName)
    {
        try
        {
            JsonNode node = JsonNode.Parse(json, documentOptions: DocumentOptions)
                ?? throw new InvalidDataException($"Gauntlet file {fileName} is empty.");
            RejectNullProperties(node, fileName, "$");
            return node;
        }
        catch (JsonException ex)
        {
            throw Invalid(fileName, ex);
        }
    }

    /// <exception cref="InvalidDataException">The JSON does not fit <typeparamref name="T"/>.</exception>
    internal static T Read<T>(JsonNode node, string fileName)
    {
        try
        {
            return node.Deserialize<T>(Options)
                ?? throw new InvalidDataException($"Gauntlet file {fileName} is empty.");
        }
        catch (JsonException ex)
        {
            throw Invalid(fileName, ex);
        }
    }

    // A property that is present but null is a defect everywhere: absent means absent, and the rules
    // files and lines files never mean "null" by anything else.
    private static void RejectNullProperties(JsonNode? node, string fileName, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    if (property.Value is null)
                    {
                        throw new InvalidDataException($"Gauntlet file {fileName} has a null value at {path}.{property.Key}; leave the property out instead.");
                    }

                    RejectNullProperties(property.Value, fileName, $"{path}.{property.Key}");
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    RejectNullProperties(item, fileName, path + "[]");
                }

                break;
        }
    }

    private static InvalidDataException Invalid(string fileName, JsonException ex) =>
        new($"Gauntlet file {fileName} is not valid JSON: {ex.Message}", ex);
}
