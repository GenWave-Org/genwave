namespace GenWave.Tts;

using System.Text.Json;

/// <summary>
/// The ONE seam that parses raw <c>Tts:Pronunciations</c>-shaped JSON into a declared (not yet
/// compiled) rule list, and serializes one back (SPEC F97.3) — shared by
/// <see cref="PronunciationRuleProvider"/> (the render-path snapshot) and the rules API controller
/// (<c>GenWave.Host.Api.PronunciationsController</c>, T144), so the two can never disagree about what
/// counts as malformed. T144 review finding: a narrower catch at the write path (e.g. only
/// <see cref="JsonException"/>) can 500 on the exact input the render path degrades from with a WARN
/// (STJ can throw <see cref="NotSupportedException"/>/<see cref="InvalidOperationException"/> for some
/// malformed shapes, not only <see cref="JsonException"/>) — one seam, one posture, everywhere.
///
/// <para>
/// <see cref="ParseDeclared"/>'s catch is DELIBERATELY BROAD: <c>Tts:Pronunciations</c> is operator-
/// authored data, never trusted deployment topology, so ANY deserialization surprise — malformed
/// JSON, or a null array element STJ happily produces from e.g. <c>"[null]"</c> — must degrade to an
/// empty declared list rather than propagate. The returned list is NOT null-filtered (a null element
/// survives, exactly mirroring <see cref="PronunciationRuleSet.Create"/>'s own
/// <c>rule is not null</c> filter one level down) so a caller that needs the RAW declared count (the
/// provider's own "declared N, compiled M" honesty, F97.5) still gets it; a caller that only needs a
/// null-safe list filters it itself.
/// </para>
///
/// <para>
/// This seam does no logging of its own — <c>Fault</c> carries the causing exception back so each
/// caller logs through its OWN injected logger (the provider's category differs from the
/// controller's, and a shared static logger here would blur that).
/// </para>
/// </summary>
public static class PronunciationRuleJson
{
    static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    static readonly JsonSerializerOptions WriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Parses <paramref name="raw"/> into its declared rule list. Blank/whitespace-only input, or
    /// valid JSON that resolves to a null array (e.g. the literal <c>"null"</c>), degrades to an empty
    /// list with no fault — "no rules configured" is not an error. A genuine deserialization failure
    /// returns the causing exception as <c>Fault</c> alongside an empty list.
    /// </summary>
    public static (IReadOnlyList<PronunciationRule> Rules, Exception? Fault) ParseDeclared(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ([], null);

        try
        {
            var rules = JsonSerializer.Deserialize<List<PronunciationRule>>(raw, ReadOptions);
            return (rules ?? [], null);
        }
        catch (Exception ex)
        {
            return ([], ex);
        }
    }

    /// <summary>Serializes a declared rule list back to the wire shape <see cref="ParseDeclared"/>
    /// reads (camelCase <c>{pattern, word, ipa}</c> objects) — the write half of the one seam.</summary>
    public static string Serialize(IReadOnlyList<PronunciationRule> rules) =>
        JsonSerializer.Serialize(rules, WriteOptions);
}
