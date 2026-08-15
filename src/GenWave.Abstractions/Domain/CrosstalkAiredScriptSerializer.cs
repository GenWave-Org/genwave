using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenWave.Core.Domain;

/// <summary>
/// The one canonical (de)serialization for <see cref="CrosstalkAiredScript"/> (SPEC F127.11, STORY-329,
/// PLAN T287) — camelCase field names
/// (<c>{"lines":[{"speaker":"Host","text":"...","isInterjection":false}]}</c>), mirroring
/// <see cref="BoothLogPickStampSerializer"/>'s own "one true (de)serialization" discipline one seam
/// over. Every writer/reader of a <see cref="SegmentKind.Crosstalk"/> row's <c>station.booth_log.pick</c>
/// jsonb MUST go through this.
///
/// <para>
/// <see cref="CrosstalkSpeaker"/> is written as its string name (review round-2 finding F-A), not the
/// enum's default numeric position — the doc's own canonical example above pins
/// <c>"speaker":"Host"</c>, not <c>"speaker":0</c>. Numeric encoding would positionally couple the
/// durable jsonb to <see cref="CrosstalkSpeaker"/>'s declaration order (any reordering — or a THIRD
/// role, this enum's own remarks note there's deliberately never one — silently reinterprets every
/// already-stored row) and would make a doc-shaped row (<c>"speaker":"Host"</c>) fail to deserialize.
/// </para>
/// </summary>
public static class CrosstalkAiredScriptSerializer
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes <paramref name="script"/> to the canonical camelCase JSON wire shape.</summary>
    public static string Serialize(CrosstalkAiredScript script) => JsonSerializer.Serialize(script, Options);

    /// <summary>
    /// Deserializes <paramref name="json"/> back into a <see cref="CrosstalkAiredScript"/>, or
    /// <see langword="null"/> when <paramref name="json"/> is the JSON literal <c>null</c> OR is
    /// off-schema (round-2 review F9 — the sibling <c>BoothLogPickStampSerializer</c>'s own documented
    /// trap: JSON binds a record's constructor parameters by reflection, not through the record's own
    /// constructor, so <c>"{}"</c> — every property missing — deserializes to a record whose
    /// <see cref="CrosstalkAiredScript.Lines"/> is <see langword="null"/> despite that member's own
    /// non-nullable annotation, nothing here enforces it). Validated HERE, at the one canonical
    /// deserialization, rather than leaving every caller to repeat its own null-<c>Lines</c> check —
    /// unlike the sibling serializer, whose callers still do that themselves.
    /// </summary>
    public static CrosstalkAiredScript? Deserialize(string json)
    {
        var script = JsonSerializer.Deserialize<CrosstalkAiredScript>(json, Options);
        return script?.Lines is not null ? script : null;
    }
}
