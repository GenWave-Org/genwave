namespace GenWave.Host.Theming;

/// <summary>
/// One font role (display or sans) a theme declares: the family name it names, plus the vendored
/// <see cref="ThemeFontAsset"/>s that actually serve it. A theme with an empty asset list would be
/// naming a face nothing backs — <see cref="ThemeManifestParser"/> rejects that at load.
/// </summary>
public sealed record ThemeFontFace(string Family, IReadOnlyList<ThemeFontAsset> Assets)
{
    /// <summary>
    /// Structural equality over <see cref="Assets"/>. The compiler-synthesized record equality would
    /// otherwise compare it by REFERENCE — <see cref="IReadOnlyList{T}"/> carries no value equality
    /// of its own — so two separately-parsed, asset-for-asset identical faces would never compare
    /// <c>Equal</c>. <see cref="ThemeFontAsset"/> itself holds only strings, so its own
    /// compiler-synthesized equality is already structural; this only needs to compare the list.
    /// </summary>
    public bool Equals(ThemeFontFace? other) =>
        other is not null &&
        string.Equals(Family, other.Family, StringComparison.Ordinal) &&
        Assets.SequenceEqual(other.Assets);

    /// <inheritdoc cref="Equals(ThemeFontFace?)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Family, StringComparer.Ordinal);
        foreach (var asset in Assets)
            hash.Add(asset);

        return hash.ToHashCode();
    }
}
