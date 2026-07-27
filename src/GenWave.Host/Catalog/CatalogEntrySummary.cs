namespace GenWave.Host.Catalog;

/// <summary>
/// One entry on the Persona Catalog shelf (SPEC F90.2), as listed in a validated index.json —
/// metadata and file pointers only. The card/meta CONTENT itself is fetched, hash-verified, and
/// cached separately, per slug, by <see cref="CatalogProxyService.GetEntryAsync"/> (SPEC F90.3,
/// F90.4) — never eagerly for the whole shelf just to build this summary.
///
/// <see cref="BestFor"/> is empty (never null) when index.json omits the optional field (F90.2's
/// own "tolerate + expose bestFor[] when present" rule) — an absent bag and an empty one are the
/// same "nothing to show" state to every consumer, so callers never need a null check.
/// </summary>
public sealed record CatalogEntrySummary(
    string Slug,
    CatalogAudience Audience,
    IReadOnlyList<string> BestFor,
    CatalogFileRef Card,
    CatalogFileRef Meta);
