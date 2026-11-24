using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenWave.Core.TalkRules;

/// <summary>
/// Memos keyed by rule id, kept sorted by id so equality never depends on the order shapers finished.
/// The only public reader is <see cref="TalkSnapshot.MemoOf"/>: a rule reads only its own (F220.8).
/// </summary>
[JsonConverter(typeof(TalkMemosConverter))]
public sealed record TalkMemos
{
    internal TalkMemos(TalkList<TalkMemo> entries) => Entries = entries;

    public static TalkMemos Empty { get; } = new(TalkList<TalkMemo>.Empty);

    internal TalkList<TalkMemo> Entries { get; }

    /// <summary>A copy with <paramref name="ruleId"/>'s memo set (replacing any earlier one).</summary>
    public TalkMemos With(string ruleId, JsonElement memo) =>
        new(new TalkList<TalkMemo>(
            Entries.Where(e => e.RuleId != ruleId)
                .Append(new TalkMemo(ruleId, Normalize(memo)))
                .OrderBy(e => e.RuleId, StringComparer.Ordinal)));

    /// <summary>
    /// The canonical spelling of a memo: minified, object keys sorted ordinally at every depth. A jsonb column
    /// reorders keys and respaces text; normalising on write and again on read makes a memo that went through one
    /// read back identical. Numbers keep their source spelling: an exponent form (<c>1E-07</c>) is not
    /// canonicalised and jsonb rewrites it, so only text equality breaks there; rules read values, not text.
    /// </summary>
    internal static string Normalize(JsonElement memo)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, memo);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    internal JsonElement? Find(string ruleId)
    {
        foreach (var entry in Entries)
        {
            if (entry.RuleId == ruleId)
            {
                using var doc = JsonDocument.Parse(entry.Json);
                return doc.RootElement.Clone();
            }
        }

        return null;
    }
}
