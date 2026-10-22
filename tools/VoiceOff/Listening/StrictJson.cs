using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenWave.VoiceOff.Listening;

/// <summary>
/// The fail-closed reader for the listening files and the run files score reads (run.json, gates.json): duplicate keys, unknown members, missing members, wrong types and numeric enums are all
/// refused. Enums read and write as camelCase strings (<c>"a"</c>, <c>"tie"</c>, <c>"plain"</c>).
/// </summary>
internal static class StrictJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <exception cref="JsonException">The text is not exactly one valid <typeparamref name="T"/>.</exception>
    internal static T Read<T>(string json)
        where T : class =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("The value is null.");
}
