namespace GenWave.Host.Catalog;

using System.Text.Json;

/// <summary>
/// The hardened, null-tolerant deserializer for a <see cref="CatalogAvatarPackManifest"/> (SPEC
/// F128.1, PLAN T292) — mirrors <see cref="CatalogFontManifestSerializer"/>'s own idiom (the T194
/// hardening applied to a font pack's manifest): reads into an ephemeral, all-nullable
/// <see cref="CatalogAvatarPackManifestJson"/> projection first, rather than deserializing straight
/// into <see cref="CatalogAvatarPackManifest"/>'s own non-nullable properties (which
/// <c>System.Text.Json</c> would silently leave <see langword="null"/> for a missing field despite the
/// C# type saying otherwise). Every required field (<c>packName</c>, and each <c>items[]</c>
/// element's own <c>name</c>/<c>file</c>) is checked present and non-empty; a malformed document, or a
/// missing/wrongly-shaped required field, degrades to <see langword="null"/> (never throws) — the same
/// "a hostile or broken origin manifest can only ever cost this projection nothing shown, never a
/// 500" posture <see cref="CatalogFontManifestSerializer.Deserialize"/> already holds.
///
/// <para>
/// NO <c>Serialize</c> (deliberate asymmetry with <see cref="CatalogFontManifestSerializer"/>): this
/// app never WRITES an avatar pack manifest — packs are catalog-authored content this app only ever
/// reads through the guarded proxy door (SPEC F90.2-F90.4) — so a byte-stable round-trip contract has
/// no real caller yet. Adding one now, with no golden fixture to pin it against, would be exactly the
/// speculative surface YAGNI rules out; the day a real writer exists (or genwave-catalog commits a
/// golden fixture this app pins byte-stability against, the <c>golden.font.json</c>/T177 precedent),
/// it is a small, additive follow-up, not a rework of this type.
/// </para>
/// </summary>
public static class CatalogAvatarPackManifestSerializer
{
    /// <summary>Case-insensitive read options (mirrors <c>CatalogIndexValidator</c>'s own untrusted-parsing options) — leniency on the READ side only.</summary>
    static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static CatalogAvatarPackManifest? Deserialize(string json)
    {
        CatalogAvatarPackManifestJson? raw;
        try
        {
            raw = JsonSerializer.Deserialize<CatalogAvatarPackManifestJson>(json, ParseOptions);
        }
        catch (JsonException)
        {
            // Malformed JSON, or a shape Deserialize can't convert (e.g. an `items` leaf typed as an
            // object instead of an array) — degrade to "no manifest", never throw out of a
            // detail-projection call.
            return null;
        }

        if (raw is not { PackName: { Length: > 0 } packName })
            return null;

        if (raw.Items is not { Count: > 0 } rawItems)
            return null;

        var items = new List<CatalogAvatarPackItem>(rawItems.Count);
        foreach (var rawItem in rawItems)
        {
            if (rawItem is not { Name: { Length: > 0 } name, File: { Length: > 0 } file })
                return null;

            items.Add(new CatalogAvatarPackItem(name, file, rawItem.SuggestedPersona));
        }

        return new CatalogAvatarPackManifest(packName, items);
    }

    /// <summary>Ephemeral, all-nullable projection of an untrusted <c>.avatar.json</c> document — nothing here is trusted until <see cref="Deserialize"/> checks it field by field.</summary>
    sealed record CatalogAvatarPackManifestJson
    {
        public string? PackName { get; init; }
        public IReadOnlyList<CatalogAvatarPackItemJson>? Items { get; init; }
    }

    /// <summary>Ephemeral, all-nullable projection of one raw <c>items[]</c> element.</summary>
    sealed record CatalogAvatarPackItemJson
    {
        public string? Name { get; init; }
        public string? File { get; init; }
        public string? SuggestedPersona { get; init; }
    }
}
