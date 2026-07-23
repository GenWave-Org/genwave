using System.Text.Json;

namespace GenWave.Core.Domain;

/// <summary>
/// The one canonical (de)serialization for <see cref="BoothLogPickStamp"/> (SPEC F86.1, STORY-217,
/// PLAN T73) — camelCase field names, matching the spec'd wire shape
/// (<c>{"firedRules":[{"summary":...,"weight":...}],"isExploration":bool}</c>) exactly. Every writer
/// and reader of <c>station.booth_log.pick</c> MUST go through this — mirrors
/// <see cref="PersonaCardSerializer"/>'s own "one true options instance" discipline.
/// </summary>
public static class BoothLogPickStampSerializer
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string Serialize(BoothLogPickStamp stamp) => JsonSerializer.Serialize(stamp, Options);

    public static BoothLogPickStamp? Deserialize(string json) =>
        JsonSerializer.Deserialize<BoothLogPickStamp>(json, Options);
}
