namespace GenWave.Host.Catalog;

/// <summary>
/// A font pack's <c>.font.json</c> manifest content (SPEC F104.1/F104.2) — family, its 1–2 faces,
/// and the licence/provenance fields FONTS.md's own provenance record already establishes for the
/// app's vendored set (<c>sourceUrl</c>/<c>license</c>/<c>version</c>/<c>subset</c>), now the
/// CATALOG's own cross-repo shape for a pack (T193; mirrors <c>ThemeManifest</c>/<c>golden.theme.json</c>'s
/// T177 precedent: authored here first, staged for genwave-catalog to commit byte-for-byte
/// identical in a later task).
///
/// <para>
/// Deliberately carries no <c>slug</c> of its own — unlike <see cref="Theming.ThemeManifest"/>
/// (which IS looked up by its own embedded slug once stored), a font pack's identity is the
/// catalog index entry's slug (SPEC F104.5's <c>imported_from</c>), the same "slug lives on the
/// index entry, not the document" shape a persona card already has.
/// </para>
///
/// <para>
/// This type is parse/serialize-only today — no consumer wires it in yet (<see cref="CatalogIndexValidator"/>
/// checks only a font entry's manifest PATH shape, never its content; T194's meta projection and
/// beyond are what will eventually read it). Its whole job right now is being the byte-stable shape
/// both this app's <c>Fixtures/golden.font.json</c> and genwave-catalog's own future commit pin
/// against — see <see cref="CatalogFontManifestSerializer"/>.
/// </para>
/// </summary>
public sealed record CatalogFontManifest(
    string Family,
    IReadOnlyList<CatalogFontManifestFile> Files,
    string License,
    string SourceUrl,
    string? Version,
    string Subset)
{
    /// <summary>
    /// Structural equality over <see cref="Files"/> — mirrors <c>ThemeFontFace</c>'s own remarks
    /// (Theming/ThemeFontFace.cs): the compiler-synthesized record equality would otherwise compare
    /// <see cref="IReadOnlyList{T}"/> by REFERENCE, so two separately-parsed, face-for-face identical
    /// manifests would never compare <c>Equal</c>. <see cref="CatalogFontManifestFile"/> itself holds
    /// only strings/an int, so its own compiler-synthesized equality is already structural; this only
    /// needs to compare the list.
    /// </summary>
    public bool Equals(CatalogFontManifest? other) =>
        other is not null &&
        string.Equals(Family, other.Family, StringComparison.Ordinal) &&
        Files.SequenceEqual(other.Files) &&
        string.Equals(License, other.License, StringComparison.Ordinal) &&
        string.Equals(SourceUrl, other.SourceUrl, StringComparison.Ordinal) &&
        string.Equals(Version, other.Version, StringComparison.Ordinal) &&
        string.Equals(Subset, other.Subset, StringComparison.Ordinal);

    /// <inheritdoc cref="Equals(CatalogFontManifest?)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Family, StringComparer.Ordinal);
        foreach (var file in Files)
            hash.Add(file);
        hash.Add(License, StringComparer.Ordinal);
        hash.Add(SourceUrl, StringComparer.Ordinal);
        hash.Add(Version, StringComparer.Ordinal);
        hash.Add(Subset, StringComparer.Ordinal);

        return hash.ToHashCode();
    }
}
