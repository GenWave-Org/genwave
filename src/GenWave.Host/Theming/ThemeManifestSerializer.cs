namespace GenWave.Host.Theming;

using System.Text.Json;

/// <summary>
/// The one canonical serialization for a <see cref="ThemeManifest"/> (STORY-269 AC5): camelCase
/// property names — matching every shipped <c>themes/*.json</c> manifest and
/// <see cref="ThemeManifestParser"/>'s own deserialization naming policy — and unindented, so a
/// parse-then-reserialize round trip is byte-stable. This is the format contract
/// <c>Fixtures/golden.theme.json</c> pins for both this repo and the future <c>genwave-catalog</c>
/// repo (T178+): the SAME shape either side commits, not a C#-flavored projection of it. Mirrors
/// <c>PersonaCardSerializer</c>'s naming-policy choice for the same reason: the manifest IS the
/// interchange format, so a second, differently-configured <see cref="JsonSerializerOptions"/>
/// instance anywhere else would silently break the byte-stable guarantee.
///
/// <see cref="ThemeManifestParser"/> never deserializes THROUGH this type — it reads an untrusted
/// document into its own ephemeral <c>*Json</c> projection and rejects it field-by-field before a
/// domain <see cref="ThemeManifest"/> exists at all (see that type's own remarks). This type is
/// therefore serialize-only today: Layer A never writes a manifest back out, so a matching
/// <c>Deserialize</c> half would have no caller yet.
/// </summary>
public static class ThemeManifestSerializer
{
    /// <summary>camelCase, unindented — the exact configuration STORY-269 AC5 pins byte-stability
    /// against.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string Serialize(ThemeManifest manifest) => JsonSerializer.Serialize(manifest, Options);
}
