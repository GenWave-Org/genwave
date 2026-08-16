namespace GenWave.Host.Icons;

/// <summary>
/// Every outcome of <see cref="IconPackDefinitionParser.Validate"/> (SPEC F130.1/F130.2, STORY-337,
/// PLAN T302). Closed hierarchy (private base constructor) — mirrors
/// <c>GenWave.Host.Catalog.CatalogIndexFetchResult</c>'s own shape — so a caller (PLAN T303's install
/// route) switches over it exhaustively, no discard arm.
/// </summary>
public abstract record IconPackValidationResult
{
    private IconPackValidationResult() { }

    /// <summary>
    /// The definition passed every whitelist/grammar/bound gate. <see cref="IgnoredNames"/> lists,
    /// sorted ordinally, every icon name present in the definition but outside
    /// <see cref="IconNameContract.Names"/> (SPEC F130.2) — install still succeeds; this type only
    /// carries the fact, PLAN T303's own install route is what turns it into the one WARN log line per
    /// name.
    /// </summary>
    public sealed record Valid(IconPackDefinition Definition, IReadOnlyList<string> IgnoredNames) : IconPackValidationResult;

    /// <summary>Rejected. <see cref="Reason"/> always names the specific rule that failed — never a
    /// bare "invalid pack" (mirrors <c>Theming.ThemeManifestException</c>'s own "always names the
    /// theme, and where mode/token-scoped, both" discipline, applied to icon packs).</summary>
    public sealed record Invalid(string Reason) : IconPackValidationResult;
}
