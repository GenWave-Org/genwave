namespace GenWave.Host.Catalog;

/// <summary>
/// One entry on the community catalog shelf (SPEC F90.2, generalised to multiple kinds by F103.2),
/// as listed in a validated index.json — metadata and file pointers only. The manifest/meta
/// CONTENT itself is fetched, hash-verified, and cached separately, per slug, by
/// <see cref="CatalogProxyService.GetEntryAsync"/> (SPEC F90.3, F90.4) — never eagerly for the
/// whole shelf just to build this summary.
///
/// <see cref="Kind"/> is the F103.1 discriminator (a persona entry authored before the field
/// existed already resolved to <see cref="CatalogEntryKind.Persona"/> by the time this is
/// constructed — see <see cref="CatalogIndexValidator"/>). <see cref="Manifest"/> is the entry's
/// primary document — a persona's <c>.persona.json</c> card today, a theme's <c>.theme.json</c>
/// once that kind ships — while <see cref="Meta"/> stays the same shape for every kind.
///
/// <see cref="BestFor"/> is empty (never null) when index.json omits the optional field (F90.2's
/// own "tolerate + expose bestFor[] when present" rule) — an absent bag and an empty one are the
/// same "nothing to show" state to every consumer, so callers never need a null check.
/// </summary>
public sealed record CatalogEntrySummary(
    string Slug,
    CatalogEntryKind Kind,
    CatalogAudience Audience,
    IReadOnlyList<string> BestFor,
    CatalogFileRef Manifest,
    CatalogFileRef Meta);
