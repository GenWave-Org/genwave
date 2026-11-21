namespace GenWave.Tts;

using System.Text.Json;

/// <summary>
/// One speaker's or setting's voice on each engine, <c>{engine: voiceId}</c> (SPEC F219.7/F219.8). Engine names
/// compare case-insensitively, like the registry. An empty engine name means <c>kokoro</c>: every card written
/// before engines were a list carries <c>Voice.Engine = ""</c>, and an explicit <c>kokoro</c> entry wins over it.
/// </summary>
public sealed class VoiceMap
{
    /// <summary>The setting that holds the station's own voice (a bare id or an engine map); what a voice-missing row names.</summary>
    public const string StationVoiceSetting = "Station:Voice";

    /// <summary>The setting key holding the ads announcer's voice (a bare id or an engine map).</summary>
    public const string AnnouncerVoiceSetting = "Station:Ads:AnnouncerVoice";

    /// <summary>The setting key holding the ads cast voice pool.</summary>
    public const string CastVoicesSetting = "Station:Ads:CastVoices";

    readonly Dictionary<string, string> byEngine = new(StringComparer.OrdinalIgnoreCase);

    VoiceMap(IEnumerable<KeyValuePair<string, string>> voices)
    {
        var legacy = new List<string>();
        foreach (var (engine, voiceId) in voices)
        {
            if (engine.Length == 0)
                legacy.Add(voiceId);
            else
                byEngine.TryAdd(engine, voiceId);
        }

        foreach (var voiceId in legacy)
            byEngine.TryAdd(VoiceEngineProfiles.KokoroName, voiceId);
    }

    /// <summary>The engines this map has a voice for (empty-name entries already read as kokoro).</summary>
    public IReadOnlyCollection<string> Engines => byEngine.Keys;

    /// <summary>The voice for <paramref name="engineName"/>, or null when the map has none.</summary>
    public string? VoiceFor(string engineName) => byEngine.GetValueOrDefault(engineName);

    /// <summary>The voice for the first engine by name (ordinal, case-insensitive), or null when the map is empty.</summary>
    public string? FirstVoice() =>
        byEngine.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Select(e => e.Value).FirstOrDefault();

    /// <summary>Every voice id in the map, whatever its engine.</summary>
    public IReadOnlyCollection<string> VoiceIds => byEngine.Values;

    /// <summary>
    /// The map written back as a voice setting value: the bare id when kokoro is its only engine, else a compact
    /// JSON object (engines in name order). <see cref="TryParseSetting"/> reads either form back to an equal map.
    /// </summary>
    public string ToSettingValue() =>
        byEngine is { Count: 1 } && byEngine.TryGetValue(VoiceEngineProfiles.KokoroName, out var only)
            ? only
            : JsonSerializer.Serialize(byEngine.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(e => e.Key, e => e.Value));

    /// <summary>True when both maps give the same voice id on any one engine (ids exact, engines ignoring case).</summary>
    public bool SharesVoiceWith(VoiceMap other) =>
        byEngine.Any(e => other.byEngine.TryGetValue(e.Key, out var theirs) && string.Equals(e.Value, theirs, StringComparison.Ordinal));

    /// <summary>A map over an existing engine-to-voice dictionary, e.g. <c>PersonaCard.VoicesByEngine()</c>.</summary>
    public static VoiceMap FromEngineVoices(IReadOnlyDictionary<string, string> voices) => new(voices);

    /// <summary>True when a voice setting is written as an engine map (a JSON object) rather than a bare id.</summary>
    public static bool IsMapSetting(string value) => value.TrimStart().StartsWith('{');

    /// <summary>
    /// The one parser for a voice setting (F219.8): a bare id (meaning kokoro) or a JSON object of engine name to
    /// non-blank voice id. Null on any malformation, a blank value, an empty object, a blank key, or two keys that
    /// differ only in case. Whether the engine names are registered is the caller's check.
    /// </summary>
    public static VoiceMap? TryParseSetting(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (!IsMapSetting(trimmed))
            return new VoiceMap([new(VoiceEngineProfiles.KokoroName, trimmed)]);

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var entries = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(property.Value.GetString())
                    || string.IsNullOrWhiteSpace(property.Name)
                    || !seen.Add(property.Name))
                    return null;

                entries.Add(new(property.Name, property.Value.GetString() ?? ""));
            }

            return entries.Count == 0 ? null : new VoiceMap(entries);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The most speakers a cast setting may name (F170.1's ceiling).</summary>
    public const int CastMaxCount = 16;

    /// <summary>
    /// The one parser for <c>Station:Ads:CastVoices</c> (F219.8): the legacy comma list of kokoro ids, or a JSON
    /// array whose entries are each a bare id (kokoro) or an engine map. Ids are kept exactly as written, so a
    /// padded id still fails the caller's id-shape rule. Null on any malformation, zero or more than
    /// <see cref="CastMaxCount"/> speakers, a blank entry, or two speakers sharing a voice on any engine (engine
    /// names ignoring case, ids exact). Whether engine names are registered and ids well shaped is the caller's check.
    /// </summary>
    public static IReadOnlyList<VoiceMap>? TryParseCastSetting(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var cast = trimmed[0] == '[' ? ParseCastArray(trimmed) : ParseCastCommaList(value);
        return cast is { Count: > 0 and <= CastMaxCount } && HasNoSharedVoice(cast) ? cast : null;
    }

    static List<VoiceMap>? ParseCastCommaList(string value)
    {
        var parts = value.Split(',', CastMaxCount + 1);
        return parts.Length > CastMaxCount || parts.Any(string.IsNullOrWhiteSpace)
            ? null
            : parts.Select(p => new VoiceMap([new(VoiceEngineProfiles.KokoroName, p)])).ToList();
    }

    static List<VoiceMap>? ParseCastArray(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() > CastMaxCount)
                return null;

            var cast = new List<VoiceMap>();
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var speaker = entry.ValueKind switch
                {
                    JsonValueKind.String when !string.IsNullOrWhiteSpace(entry.GetString())
                        => new VoiceMap([new(VoiceEngineProfiles.KokoroName, entry.GetString() ?? "")]),
                    JsonValueKind.Object => TryParseSetting(entry.GetRawText()),
                    _ => null,
                };
                if (speaker is null)
                    return null;

                cast.Add(speaker);
            }

            return cast;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static bool HasNoSharedVoice(List<VoiceMap> cast)
    {
        var seen = new HashSet<(string Engine, string Voice)>();
        return cast.SelectMany(m => m.byEngine)
            .All(e => seen.Add((e.Key.ToUpperInvariant(), e.Value)));
    }
}
