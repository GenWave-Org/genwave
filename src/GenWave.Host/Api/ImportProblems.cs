using Microsoft.AspNetCore.Mvc;

namespace GenWave.Host.Api;

/// <summary>
/// Shared <see cref="ProblemDetails"/> factories for the portable-JSON import/preview routes (SPEC
/// F79.6, F103.5, F103.6, F103.10; <see cref="ThemesImportController"/>,
/// <see cref="ThemePreviewController"/>) — the problem shapes both controllers' size-cap,
/// malformed-manifest, schema-major, and font-provenance gates return were a verbatim-duplicated copy
/// each (including the size-cap copy string, review finding T186) until this type. Lives next to
/// <see cref="BoundedImportBodyReader"/> and <see cref="ThemeSchemaVersionGate"/>, the shared CONTROLS
/// these factories describe the failure of — same "shared control, one home" idiom those types' own
/// remarks already established (PLAN T184 review F4; carried forward to the schema-major and
/// font-provenance gates when the preview route grew them too, Dean's directive 2026-08-05).
/// </summary>
internal static class ImportProblems
{
    /// <summary>The shared 413 body both routes return when
    /// <see cref="BoundedImportBodyReader.ReadBoundedBodyAsync"/> reports oversized.</summary>
    public static ProblemDetails Oversized() => new()
    {
        Status = StatusCodes.Status413PayloadTooLarge,
        Title  = "Payload too large.",
        Detail = $"Theme manifests are capped at {BoundedImportBodyReader.MaxImportBytes / 1024} KB.",
    };

    /// <summary>The shared 400 body both routes return when
    /// <see cref="ThemeManifestParser.Parse"/> throws a <see cref="ThemeManifestException"/> —
    /// <paramref name="detail"/> carries that exception's own message verbatim.</summary>
    public static ProblemDetails MalformedManifest(string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Malformed theme manifest.",
        Detail = detail,
    };

    /// <summary>The shared 400 body both routes return when
    /// <see cref="ThemeSchemaVersionGate.ExtractSchemaVersion"/> finds a <c>schemaVersion</c> over
    /// <see cref="ThemeSchemaVersionGate.CurrentSchemaVersion"/> — naming both versions verbatim (SPEC
    /// F103.6 AC6), the exact phrase both routes' specs assert.</summary>
    public static ProblemDetails NewerSchema(int manifestSchemaVersion) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unsupported schema version.",
        Detail =
            $"Theme manifest schema version {manifestSchemaVersion} is newer than this station's " +
            $"supported version {ThemeSchemaVersionGate.CurrentSchemaVersion}.",
    };

    /// <summary>The shared 400 body both routes return when
    /// <see cref="ThemeSchemaVersionGate.ExtractSchemaVersion"/> reports a present-but-unparsable
    /// <c>schemaVersion</c> (a JSON string, a fraction, an overflowing integer).</summary>
    public static ProblemDetails UnreadableSchemaVersion() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid schema version.",
        Detail = "schemaVersion, when present, must be a whole number.",
    };

    /// <summary>The shared 400 body both routes return when
    /// <see cref="ThemeFontProvenanceValidator.Validate"/> throws (SPEC F103.10, PLAN T188) —
    /// <paramref name="detail"/> carries that exception's own message verbatim, either an
    /// unvendored-face name (and the whole vendored set) or an over-ceiling byte total.</summary>
    public static ProblemDetails UnvendoredFont(string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Theme fonts rejected.",
        Detail = detail,
    };
}
