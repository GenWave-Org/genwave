namespace GenWave.Host.Catalog;

using System.Text.Json;

/// <summary>
/// The one canonical (de)serialization for a <see cref="CatalogFontManifest"/> (SPEC F104.1/F104.2,
/// T193) — camelCase property names, unindented, so a parse-then-reserialize round trip is
/// byte-stable. This is the format contract <c>Fixtures/golden.font.json</c> pins for both this repo
/// and the future genwave-catalog repo (T195+), the T177 precedent (<c>ThemeManifestSerializer</c>)
/// applied to the font kind. Symmetric — unlike <c>ThemeManifestSerializer</c>, no hardened,
/// field-by-field validating parser exists for this format yet (nothing in this app reads untrusted
/// font-manifest CONTENT today; <see cref="CatalogIndexValidator"/> only ever checks a font entry's
/// manifest PATH shape) — that hardening is a later task's job once a real consumer (T194's meta
/// projection, T199's install) needs to trust bytes an origin served.
///
/// <para>
/// FOR T194 (recorded here, not yet built — S2/note review finding): once a real fetch/verify
/// transport reads origin bytes into this shape, <see cref="Deserialize"/> must become
/// null-tolerant the same way <see cref="CatalogIndexValidator.TryParsePreview"/>/<c>TryValidateAssetRef</c>
/// already are for index.json content — a malformed or wrong-typed manifest document must degrade,
/// never throw, out of a bulk-content path. AND the transport must size-cap the actual stream read
/// against <c>min(declared <see cref="CatalogAssetRef.Bytes"/>, a house MaxAssetBytes constant)</c>
/// — the declared size alone is untrusted origin content and must never be the ONLY bound a caller
/// reads against.
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

    public static string Serialize(CatalogFontManifest manifest) => JsonSerializer.Serialize(manifest, Options);

    public static CatalogFontManifest? Deserialize(string json) =>
        JsonSerializer.Deserialize<CatalogFontManifest>(json, Options);
}
