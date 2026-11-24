using System.Text.Json;
using GenWave.Core.TalkRules;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The <c>talk_check.verdicts</c> jsonb: <c>{rule_id: {on: bool, flag: {reason, ask, evidence}|null}}</c>.
/// A switched-off rule's verdict is stored too, with <c>on</c> false (F220.29).
/// </summary>
static class TalkVerdictsJson
{
    sealed record StoredVerdict(bool On, RuleFlag? Flag);

    static readonly JsonSerializerOptions Options = Build();

    public static string Serialize(IEnumerable<RuleVerdict> verdicts) =>
        JsonSerializer.Serialize(
            verdicts.ToDictionary(v => v.RuleId, v => new StoredVerdict(v.SwitchedOn, v.Flag)), Options);

    public static TalkList<RuleVerdict> Deserialize(string json)
    {
        var stored = JsonSerializer.Deserialize<Dictionary<string, StoredVerdict>>(json, Options)
            ?? throw new JsonException("Verdicts JSON was null.");

        return new TalkList<RuleVerdict>(
            stored.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new RuleVerdict(p.Key, p.Value.On, p.Value.Flag)));
    }

    static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectNullableAnnotations = true };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
