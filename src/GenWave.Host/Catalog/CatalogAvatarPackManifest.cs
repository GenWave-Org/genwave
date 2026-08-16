namespace GenWave.Host.Catalog;

/// <summary>
/// An avatar pack's <c>.avatar.json</c> manifest content (SPEC F128.1) — mirrors
/// <see cref="CatalogFontManifest"/>'s own "ephemeral, hardened, null-tolerant" shape for a SECOND
/// assets-carrying kind: a display <see cref="PackName"/> plus its <see cref="Items"/> (each naming
/// which sibling <see cref="CatalogAssetRef"/> it wears — <see cref="CatalogAvatarPackItem"/>'s own
/// remarks). Deep PNG re-validation (magic bytes, IHDR 512², acTL reject) happens at INSTALL time
/// (PLAN T293, SPEC F128.3) — this type, and <see cref="CatalogAvatarPackManifestSerializer"/> that
/// builds it, only ever carry the manifest's own declared SHAPE, never its bytes.
/// </summary>
public sealed record CatalogAvatarPackManifest(string PackName, IReadOnlyList<CatalogAvatarPackItem> Items)
{
    /// <summary>
    /// Structural equality over <see cref="Items"/> — mirrors <see cref="CatalogFontManifest"/>'s own
    /// remarks: the compiler-synthesized record equality would otherwise compare
    /// <see cref="IReadOnlyList{T}"/> by REFERENCE, so two separately-parsed, item-for-item identical
    /// manifests would never compare <see langword="true"/>. <see cref="CatalogAvatarPackItem"/> itself
    /// holds only strings, so its own compiler-synthesized equality is already structural; this only
    /// needs to compare the list.
    /// </summary>
    public bool Equals(CatalogAvatarPackManifest? other) =>
        other is not null &&
        string.Equals(PackName, other.PackName, StringComparison.Ordinal) &&
        Items.SequenceEqual(other.Items);

    /// <inheritdoc cref="Equals(CatalogAvatarPackManifest?)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PackName, StringComparer.Ordinal);
        foreach (var item in Items)
            hash.Add(item);

        return hash.ToHashCode();
    }
}
