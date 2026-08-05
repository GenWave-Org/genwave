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
///
/// <see cref="Preview"/> (SPEC F103.4, T185) is <see langword="null"/> for every persona entry and
/// for a theme entry whose index predates the field or omits it — genuinely optional, unlike
/// <see cref="BestFor"/>'s "absent means empty" posture, since a swatch set has no meaningful empty
/// value: a caller either has five colours to paint chips with, or it has nothing to show at all.
///
/// <see cref="Assets"/> (SPEC F104.1, T193) follows <see cref="BestFor"/>'s "absent means empty"
/// posture, not <see cref="Preview"/>'s "genuinely optional" one: empty (never null) for every
/// persona/theme entry — those kinds have no assets concept at all, not merely an omitted one — and
/// non-empty for a <see cref="CatalogEntryKind.Font"/> entry, which <see cref="CatalogIndexValidator"/>
/// never constructs with zero (a pack IS its files; a font entry whose declared assets are missing
/// or malformed is skipped outright rather than admitted with an empty list).
/// </summary>
public sealed record CatalogEntrySummary(
    string Slug,
    CatalogEntryKind Kind,
    CatalogAudience Audience,
    IReadOnlyList<string> BestFor,
    CatalogFileRef Manifest,
    CatalogFileRef Meta,
    CatalogThemePreview? Preview,
    IReadOnlyList<CatalogAssetRef> Assets);
