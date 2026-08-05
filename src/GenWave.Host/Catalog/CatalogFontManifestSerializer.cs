namespace GenWave.Host.Catalog;

using System.Text.Json;

/// <summary>
/// The one canonical (de)serialization for a <see cref="CatalogFontManifest"/> (SPEC F104.1/F104.2,
/// T193) — camelCase property names, unindented, so a parse-then-reserialize round trip is
/// byte-stable. This is the format contract <c>Fixtures/golden.font.json</c> pins for both this repo
/// and the future genwave-catalog repo (T195+), the T177 precedent (<c>ThemeManifestSerializer</c>)
/// applied to the font kind.
///
/// <para>
/// HARDENED, NULL-TOLERANT DESERIALIZE (T194, closing a T193 review obligation): <see cref="Deserialize"/>
/// reads into an ephemeral, all-nullable <see cref="CatalogFontManifestJson"/> projection first — the
/// same "untrusted document → ephemeral <c>*Json</c> record → field-by-field reject-vs-accept →
/// immutable domain type" idiom <c>ThemeManifestParser</c> and <see cref="CatalogIndexValidator"/>
/// already use — rather than deserializing straight into <see cref="CatalogFontManifest"/>'s own
/// non-nullable properties (which <c>System.Text.Json</c> would silently leave <see langword="null"/>
/// for a missing field despite the C# type saying otherwise: an NRT violation waiting to happen the
/// instant a real caller reads that field). <see cref="Api.CatalogController"/>'s font-kind meta
/// projection (T194, SPEC F104.3) is this method's first real consumer of ORIGIN-fetched bytes: every
/// required field (<c>family</c>/<c>license</c>/<c>sourceUrl</c>/<c>subset</c>, and each
/// <c>files[]</c> element's own <c>role</c>/<c>file</c>/<c>weight</c>/<c>style</c>/<c>bytes</c>) is
/// now checked present, non-empty, and (for <c>bytes</c>) positive — a malformed document or a
/// missing/wrongly-shaped required field degrades to <see langword="null"/> (never throws, mirroring
/// <see cref="CatalogIndexValidator.TryValidateAssetRef"/>'s own posture), so a hostile or broken
/// origin manifest can only ever cost this projection "no family shown", never a 500. <c>version</c>
/// stays the one genuinely optional field, matching <see cref="CatalogFontManifest"/>'s own
/// <see langword="string?"/> shape.
/// </para>
///
/// <para>
/// OBLIGATION FOR T195/T196 (STORY-281 AC1 reconciliation, T194 review finding, recorded here per
/// this class's own established "obligations block" spot): the shelf listing (<c>GET
/// /api/catalog/index</c>) now admits an OPTIONAL <c>family</c> string directly on an index.json
/// entry (<see cref="CatalogIndexValidator"/>'s own <c>TryParseFamily</c>, carried onto
/// <see cref="CatalogEntrySummary.Family"/> → <c>CatalogShelfEntryDto.FontFamily</c>) — because
/// browsing the shelf never fetches a pack's <c>.font.json</c> manifest (this type's own
/// <see cref="Family"/>-bearing field lives ONLY in that fetched document today). The catalog-side
/// <c>build_index.py</c> projection (genwave-catalog repo, T195/T196) is what actually needs to
/// COPY a pack's own <see cref="CatalogFontManifest.Family"/> value onto its index.json entry's own
/// <c>family</c> field when the index is generated — until that lands, every real font entry's
/// shelf card shows no family (a graceful <see langword="null"/>, never an error), even though its
/// detail view already shows one (<see cref="Api.CatalogController.ToEntryResponse"/> reads it
/// straight off the fetched manifest, zero extra cost, unaffected by this obligation).
/// </para>
/// </summary>
public static class CatalogFontManifestSerializer
{
    /// <summary>camelCase, unindented — the exact configuration the golden fixture round trip pins
    /// byte-stability against (mirrors <c>ThemeManifestSerializer.Options</c>).</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Case-insensitive read options for <see cref="Deserialize"/>'s ephemeral projection (mirrors
    /// <see cref="CatalogIndexValidator"/>'s own untrusted-parsing options) — leniency on the READ
    /// side only; <see cref="Options"/> above stays the exact camelCase-only configuration the
    /// byte-stable round trip is pinned against.
    /// </summary>
    static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(CatalogFontManifest manifest) => JsonSerializer.Serialize(manifest, Options);

    public static CatalogFontManifest? Deserialize(string json)
    {
        CatalogFontManifestJson? raw;
        try
        {
            raw = JsonSerializer.Deserialize<CatalogFontManifestJson>(json, ParseOptions);
        }
        catch (JsonException)
        {
            // Malformed JSON, or a shape Deserialize can't convert (e.g. a `files` leaf typed as an
            // object instead of an array) — degrade to "no manifest", never throw out of a bulk
            // fetch/projection path.
            return null;
        }

        if (raw is not
            {
                Family: { Length: > 0 } family,
                License: { Length: > 0 } license,
                SourceUrl: { Length: > 0 } sourceUrl,
                Subset: { Length: > 0 } subset,
            })
            return null;

        if (raw.Files is not { Count: > 0 } rawFiles)
            return null;

        var files = new List<CatalogFontManifestFile>(rawFiles.Count);
        foreach (var rawFile in rawFiles)
        {
            if (rawFile is not
                {
                    Role: { Length: > 0 } role,
                    File: { Length: > 0 } file,
                    Weight: { Length: > 0 } weight,
                    Style: { Length: > 0 } style,
                    Bytes: { } bytes,
                } || bytes <= 0)
                return null;

            files.Add(new CatalogFontManifestFile(role, file, weight, style, bytes));
        }

        return new CatalogFontManifest(family, files, license, sourceUrl, raw.Version, subset);
    }

    /// <summary>Ephemeral, all-nullable projection of an untrusted <c>.font.json</c> document — nothing here is trusted until <see cref="Deserialize"/> checks it field by field.</summary>
    sealed record CatalogFontManifestJson
    {
        public string? Family { get; init; }
        public IReadOnlyList<CatalogFontManifestFileJson>? Files { get; init; }
        public string? License { get; init; }
        public string? SourceUrl { get; init; }
        public string? Version { get; init; }
        public string? Subset { get; init; }
    }

    /// <summary>Ephemeral, all-nullable projection of one raw <c>files[]</c> element.</summary>
    sealed record CatalogFontManifestFileJson
    {
        public string? Role { get; init; }
        public string? File { get; init; }
        public string? Weight { get; init; }
        public string? Style { get; init; }
        public int? Bytes { get; init; }
    }
}
