using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenWave.Core.TalkRules;

/// <summary>
/// The ONE codec every writer and reader of a stored <see cref="TalkSnapshot"/> goes through (F220.30).
/// camelCase properties and kebab-case <see cref="TalkKinds"/> names ("dj-line", the same spelling as the
/// <c>talk_kind</c> columns) are fixed here, so no caller can drift from them.
/// </summary>
public static class TalkSnapshotJson
{
    public static readonly JsonSerializerOptions Options = BuildOptions();

    public static string Serialize(TalkSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static TalkSnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize<TalkSnapshot>(json, Options)
        ?? throw new JsonException("Snapshot JSON was null.");

    private static JsonSerializerOptions BuildOptions()
    {
        // A stored row missing a non-nullable field fails loudly on read instead of yielding a null a rule trips over.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectNullableAnnotations = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        // Reflection-based, no source-generated context: populate the default resolver so the instance can lock.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
