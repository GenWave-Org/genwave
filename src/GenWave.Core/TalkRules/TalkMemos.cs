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

    /// <summary>The minified spelling of a memo, so a memo written and read back through <see cref="TalkSnapshotJson"/> is byte-identical (a jsonb column may reorder keys; compare verdicts, not memo text, after one).</summary>
    internal static string Normalize(JsonElement memo) => JsonSerializer.Serialize(memo);

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
