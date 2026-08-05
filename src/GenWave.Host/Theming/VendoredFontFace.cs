namespace GenWave.Host.Theming;

/// <summary>
/// One vendored font file's provenance record (FONTS.md; SPEC F103.10; PLAN T188) — the licence
/// GenWave confirmed before vendoring it, where it came from, its latin-subset step, and its byte
/// weight. One entry per <c>/fonts/{file}</c> path <see cref="ThemeManifestParser"/>'s
/// <c>FontSrcPattern</c> can ever resolve to; the GenWave-vendored curated set (SPEC F103.10) is
/// exactly the set of <see cref="Src"/> values <see cref="FontProvenanceCatalog"/> loads.
/// </summary>
public sealed record VendoredFontFace(
    string Family,
    string File,
    string SourceUrl,
    string License,
    string? Version,
    string Subset,
    long Bytes)
{
    /// <summary>The exact <c>/fonts/{File}</c> shape a theme manifest's font asset
    /// <see cref="ThemeFontAsset.Src"/> carries — the key <see cref="FontProvenanceCatalog.BySrc"/>
    /// is indexed by, so a validator lookup never rebuilds this string a second way.</summary>
    public string Src => $"/fonts/{File}";
}
