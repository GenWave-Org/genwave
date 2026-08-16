namespace GenWave.Host.Icons;

/// <summary>
/// A validated icon pack definition (SPEC F130.1, STORY-337, PLAN T302) — the typed shape of the raw
/// jsonb text a <c>GenWave.Core.Domain.IconPack</c> row's own <c>Definition</c> carries, reconstituted
/// at GenWave.Host's own edge. Mirrors <c>GenWave.Core.Domain.OwnerTheme.Definition</c>/
/// <c>Theming.ThemeManifest</c>'s own "opaque jsonb in Core, typed in Host" split (see
/// <c>OwnerTheme</c>'s own remarks) — <c>IconPack</c> lives in <c>GenWave.Core</c>, downstream of
/// nothing; this type lives in <c>GenWave.Host</c>, which is downstream of <c>GenWave.Core</c>, so a
/// caller (PLAN T303's install route) reconstitutes this shape at its own edge rather than
/// <c>GenWave.Core</c> depending on GenWave.Host's own parsing machinery. Only
/// <see cref="IconPackDefinitionParser.Validate"/> constructs one, and only after every element in
/// every icon has passed its own whitelist/grammar/finite-number gate.
/// </summary>
/// <param name="Style">The pack-level stroke width and fill mode every icon renders under (SPEC
/// F130.1).</param>
/// <param name="Icons">Every icon this definition declares, keyed by name — BOTH names inside and
/// outside <see cref="IconNameContract.Names"/> (SPEC F130.2's "a pack may cover any subset": a name
/// outside the contract is still a syntactically valid, whitelist-passing entry, simply never
/// rendered under any name a shipped UI slot uses today).
/// <see cref="IconPackValidationResult.Valid.IgnoredNames"/> is how the out-of-contract subset reaches
/// a caller separately, for the one install-time WARN PLAN T303 logs.</param>
public sealed record IconPackDefinition(
    IconPackStyle Style,
    IReadOnlyDictionary<string, IReadOnlyList<IconElement>> Icons)
{
    /// <summary>
    /// Structural equality over <see cref="Icons"/> — mirrors <c>Theming.ThemeModes</c>'s own remarks:
    /// the compiler-synthesized record equality would otherwise compare it, and each icon's own
    /// element list, by REFERENCE. <see cref="IconElement"/>'s own compiler-synthesized equality is
    /// already structural (every member is a primitive or a string), so this only needs to compare the
    /// dictionary and each value list.
    /// </summary>
    public bool Equals(IconPackDefinition? other) =>
        other is not null &&
        Style == other.Style &&
        IconsEqual(Icons, other.Icons);

    /// <inheritdoc cref="Equals(IconPackDefinition?)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Style);
        hash.Add(IconsHash(Icons));
        return hash.ToHashCode();
    }

    static bool IconsEqual(
        IReadOnlyDictionary<string, IReadOnlyList<IconElement>> left,
        IReadOnlyDictionary<string, IReadOnlyList<IconElement>> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (name, elements) in left)
        {
            if (!right.TryGetValue(name, out var otherElements) || !elements.SequenceEqual(otherElements))
                return false;
        }

        return true;
    }

    // XOR-combined, like ThemeModes.TokensHash — order-independent, so two definitions holding the
    // same icons in a different Dictionary enumeration order still hash identically (Equals above is
    // already order-independent over the icon names; GetHashCode must agree).
    static int IconsHash(IReadOnlyDictionary<string, IReadOnlyList<IconElement>> icons)
    {
        var combined = 0;
        foreach (var (name, elements) in icons)
        {
            var elementsHash = new HashCode();
            foreach (var element in elements)
                elementsHash.Add(element);

            combined ^= HashCode.Combine(name, elementsHash.ToHashCode());
        }

        return combined;
    }
}
