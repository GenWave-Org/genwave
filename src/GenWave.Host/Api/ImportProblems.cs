using Microsoft.AspNetCore.Mvc;

namespace GenWave.Host.Api;

/// <summary>
/// Shared <see cref="ProblemDetails"/> factories for the portable-JSON import/preview routes (SPEC
/// F79.6, F103.5, F103.6; <see cref="ThemesImportController"/>, <see cref="ThemePreviewController"/>)
/// — the two problem shapes both controllers' size-cap and malformed-manifest gates return were a
/// verbatim-duplicated copy each, including the size-cap copy string (review finding, T186), until
/// this type. Lives next to <see cref="BoundedImportBodyReader"/>, the shared size-cap CONTROL these
/// factories describe the failure of — same "shared control, one home" idiom that type's own remarks
/// already established for the size-cap read itself (PLAN T184 review F4).
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
}
