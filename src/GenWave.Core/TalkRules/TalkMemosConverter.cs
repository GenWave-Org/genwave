using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenWave.Core.TalkRules;

/// <summary>Stores <see cref="TalkMemos"/> as one JSON object, <c>{"ruleId": memo, ...}</c>, each memo as real JSON.</summary>
internal sealed class TalkMemosConverter : JsonConverter<TalkMemos>
{
    public override TalkMemos Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Memos must be a JSON object keyed by rule id.");
        }

        var memos = TalkMemos.Empty;
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            memos = memos.With(property.Name, property.Value);
        }

        return memos;
    }

    public override void Write(Utf8JsonWriter writer, TalkMemos value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var entry in value.Entries)
        {
            writer.WritePropertyName(entry.RuleId);
            writer.WriteRawValue(entry.Json, skipInputValidation: true);
        }

        writer.WriteEndObject();
    }
}
