namespace GenWave.Host.Theming;

/// <summary>
/// A theme's light and dark token sets — semantic token name (<c>bg</c>, <c>accent</c>, …) to CSS
/// colour value. Both modes are mandatory and, once a manifest has passed
/// <see cref="ThemeManifestParser"/>, carry the identical set of token keys: flat one-look themes
/// were rejected at design (ARCHITECTURE "Theme system") because they regress automatic
/// <c>prefers-color-scheme</c> dark and strand an OS-dark visitor in a light palette.
/// </summary>
public sealed record ThemeModes(
    IReadOnlyDictionary<string, string> Light,
    IReadOnlyDictionary<string, string> Dark)
{
    /// <summary>
    /// Structural equality over the token dictionaries. The compiler-synthesized record equality
    /// would otherwise compare <see cref="Light"/>/<see cref="Dark"/> by REFERENCE —
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/> carries no value equality of its own — so two
    /// separately-parsed, token-for-token identical manifests would never compare <c>Equal</c>, and
    /// this record's <c>ThemeManifest</c> parent would inherit the same lie transitively. T159's CSS
    /// cache and T164's resolution both key on a loaded theme, so this has to hold.
    /// </summary>
    public bool Equals(ThemeModes? other) =>
        other is not null &&
        TokensEqual(Light, other.Light) &&
        TokensEqual(Dark, other.Dark);

    /// <inheritdoc cref="Equals(ThemeModes?)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TokensHash(Light));
        hash.Add(TokensHash(Dark));
        return hash.ToHashCode();
    }

    static bool TokensEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (token, value) in left)
        {
            if (!right.TryGetValue(token, out var otherValue) || !string.Equals(value, otherValue, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    // Combines pairs with XOR rather than folding them in order, so the result is independent of
    // Dictionary<,>'s unspecified enumeration order — two dictionaries holding the same pairs in a
    // different order must still hash identically, or GetHashCode would contradict Equals above.
    static int TokensHash(IReadOnlyDictionary<string, string> tokens)
    {
        var combined = 0;
        foreach (var (token, value) in tokens)
            combined ^= HashCode.Combine(token, value);

        return combined;
    }
}
