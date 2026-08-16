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
    /// Reads the optional top-level <c>schemaVersion</c> field off <paramref name="root"/> — delegates
    /// to the shared <see cref="SchemaVersionProbe"/> (PLAN T302 review F4; this method used to hold its
    /// own verbatim copy of the extraction, until a THIRD format duplicated it too). Three outcomes,
    /// not two (PLAN T184 review F2): the field is ABSENT ⇒ <c>(null, false)</c>, treated by callers as
    /// version <see cref="CurrentSchemaVersion"/>; PRESENT and a readable <see cref="int"/> ⇒
    /// <c>(version, false)</c>; PRESENT but not a readable <see cref="int"/> — a JSON string, a
    /// fractional number, or one that overflows <see cref="int"/> — ⇒ <c>(null, true)</c>, a refusal
    /// rather than a silent "treat as absent". Kept as this type's own public entry point (rather than
    /// having both call sites below call <see cref="SchemaVersionProbe"/> directly) so this type stays
    /// the one thing both theme routes depend on for their schema-major gate.
    /// </summary>
    public static (int? Version, bool Unreadable) ExtractSchemaVersion(JsonElement root) =>
        SchemaVersionProbe.Extract(root);
}
