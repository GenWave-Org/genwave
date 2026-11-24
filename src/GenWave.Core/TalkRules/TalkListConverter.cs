using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenWave.Core.TalkRules;

/// <summary>Reads and writes one closed <see cref="TalkList{T}"/> as a plain JSON array.</summary>
internal sealed class TalkListConverter<T> : JsonConverter<TalkList<T>>
{
    public override TalkList<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? []);

    public override void Write(Utf8JsonWriter writer, TalkList<T> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize<IReadOnlyList<T>>(writer, value, options);
}
