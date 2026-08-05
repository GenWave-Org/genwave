using System.Text.Json;

namespace GenWave.Host.Api;

/// <summary>
/// Shared schema-major gate for the portable-JSON theme routes (SPEC F103.5, F103.6;
/// <see cref="ThemesImportController"/>, <see cref="ThemePreviewController"/>) — the
/// <c>schemaVersion</c> extraction <see cref="ThemesImportController"/> used to own as a private
/// static method is now a shared type both routes call, mirroring the "shared control, one home" idiom
/// <see cref="BoundedImportBodyReader"/>/<see cref="ImportProblems"/> already established at PLAN T184
/// review F4 — this is that same review finding, carried forward once the preview route grew a
/// schema-major gate of its own (Dean's directive 2026-08-05: "preview refuses what import refuses").
///
/// <para>
/// <see cref="ThemeManifest"/> carries no <c>SchemaVersion</c> field of its own — see
/// <see cref="ThemesImportController"/>'s own remarks ("Schema-major reject") for the full rationale
/// this type implements: an OPTIONAL top-level <c>schemaVersion</c> integer read straight off the raw
/// request JSON, three outcomes rather than two (PLAN T184 review F2). ABSENT (every manifest that
/// exists today, shipped or fixture) ⇒ treated as <see cref="CurrentSchemaVersion"/> and passes, at
/// zero cost to any current caller; PRESENT and over <see cref="CurrentSchemaVersion"/> ⇒ refused,
/// naming both; PRESENT but not a readable <see cref="int"/> — a JSON string, a fractional number, an
/// integer that overflows — ⇒ ALSO refused, rather than silently coerced to "absent".
/// </para>
/// </summary>
internal static class ThemeSchemaVersionGate
{
    /// <summary>The one schema major both routes currently accept — see this type's own remarks for
    /// why the constant lives here rather than on <see cref="ThemeManifest"/> itself.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Reads the optional top-level <c>schemaVersion</c> field off <paramref name="root"/>. Three
    /// outcomes, not two (PLAN T184 review F2): the field is ABSENT ⇒ <c>(null, false)</c>, treated by
    /// callers as version <see cref="CurrentSchemaVersion"/>; PRESENT and a readable <see cref="int"/>
    /// ⇒ <c>(version, false)</c>; PRESENT but not a readable <see cref="int"/> — a JSON string, a
    /// fractional number, or one that overflows <see cref="int"/> — ⇒ <c>(null, true)</c>, a refusal
    /// rather than a silent "treat as absent". Guards <paramref name="root"/>'s own
    /// <see cref="JsonElement.ValueKind"/> before calling
    /// <see cref="JsonElement.TryGetProperty(string,out JsonElement)"/>, which throws for a
    /// syntactically valid but non-object root (a bare JSON array/string/number) — that shape is left
    /// for the caller's own structural parse to report, never here.
    /// </summary>
    public static (int? Version, bool Unreadable) ExtractSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var property))
            return (null, false);

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var version)
            ? (version, false)
            : (null, true);
    }
}
