namespace GenWave.Host;

using System.Text.Json;

/// <summary>
/// Format-agnostic optional-<c>schemaVersion</c> probe (PLAN T302 review F4) — the exact three-outcome
/// extraction <see cref="Api.ThemeSchemaVersionGate"/> and <see cref="Shows.ShowManifestParser"/> each
/// used to hold as their own verbatim copy, now the ONE shared home both delegate to (and
/// <see cref="Icons.IconPackDefinitionParser"/> calls directly, having no write-route controller of its
/// own to lean a format-specific gate type on — see that type's own "SCHEMA-MAJOR" remarks).
///
/// <para>
/// An OPTIONAL top-level <c>schemaVersion</c> integer, read off an untrusted document's raw
/// <see cref="JsonElement"/> root — BEFORE any format's own structural deserialize ever runs, since a
/// newer major is free to look nothing like today's shape. ABSENT ⇒ <c>(null, false)</c>, every caller
/// treats this as its own <c>CurrentSchemaVersion</c> and passes; PRESENT and a readable <see cref="int"/>
/// ⇒ <c>(version, false)</c>; PRESENT but unreadable — a JSON string, a fractional number, an integer
/// that overflows — ⇒ <c>(null, true)</c>, a refusal rather than a silent "treat as absent". Guards
/// <see cref="JsonElement.ValueKind"/> before calling
/// <see cref="JsonElement.TryGetProperty(string,out JsonElement)"/>, which throws for a syntactically
/// valid but non-object root (a bare JSON array/string/number) — that shape is left for the caller's own
/// structural parse to report, never here.
/// </para>
///
/// <para>
/// Lives at the <c>GenWave.Host</c> root, not under <c>Api</c> — this extraction is not owned by the API
/// routing layer (<see cref="Icons.IconPackDefinitionParser"/> has no controller of its own yet, PLAN
/// T303, to justify the <c>Api</c>-namespaced home <see cref="Api.ThemeSchemaVersionGate"/> earned when
/// a SECOND theme route needed the identical extraction), nor by any one manifest format — each format
/// keeps its OWN <c>CurrentSchemaVersion</c> constant and its own newer-than-supported refusal wording
/// exactly where it already lived (Theme and Show happen to both be at major 1 today, coincidentally,
/// not because they share one version space). Mirrors the house "shared control, one home" idiom
/// <see cref="Api.BoundedImportBodyReader"/>/<see cref="Api.ImportProblems"/> already established (PLAN
/// T184 review F4) — applied here to the extraction MECHANICS a third format duplicating verbatim
/// (PLAN T302 review F4) made worth hoisting.
/// </para>
/// </summary>
internal static class SchemaVersionProbe
{
    /// <summary>
    /// Reads the optional top-level <c>schemaVersion</c> field off <paramref name="root"/>. See this
    /// type's own remarks for the exact three-outcome contract.
    /// </summary>
    public static (int? Version, bool Unreadable) Extract(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var property))
            return (null, false);

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var version)
            ? (version, false)
            : (null, true);
    }
}
