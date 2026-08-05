namespace GenWave.Host.Api;

/// <summary>
/// One row on <c>GET /api/catalog/index</c>'s shelf listing (SPEC F90.2, F103.3) — the wire
/// projection of <see cref="Catalog.CatalogEntrySummary"/>, and ONLY what that type carries:
/// <see cref="Slug"/>, <see cref="Kind"/>, <see cref="Audience"/>, <see cref="BestFor"/>.
/// Tagline/description/author/sample patter are NOT here — those live inside a
/// fetched-and-hash-verified <c>meta.json</c>, which this index-level listing never eagerly
/// fetches for every entry (SPEC F90.2's own "metadata and file pointers only" contract on
/// <see cref="Catalog.CatalogEntrySummary"/>); the Admin UI reads them per-entry via
/// <c>GET /api/catalog/entries/{slug}</c> instead.
/// </summary>
/// <param name="Slug">The catalog entry's own slug (SPEC F90.7's provenance value, once imported).</param>
/// <param name="Kind">
/// <c>"persona"</c>, <c>"theme"</c>, or <c>"font"</c> (SPEC F103.1, F103.3, widened by F104.1) —
/// lowercase, matching genwave-catalog's own schema vocabulary verbatim, so a future multi-kind
/// shelf can route each card to the right detail view/importer without re-deriving kind from
/// anything else on the entry. A font card carries no <see cref="Preview"/> and no asset list yet
/// (S1 review finding, T193): the minimal honest projection — kind/slug/audience/bestFor only —
/// until a real font shelf card design (T194+) needs more.
/// </param>
/// <param name="Audience">
/// <c>"everyone"</c> or <c>"mature"</c> — lowercase, matching genwave-catalog's own schema
/// vocabulary verbatim (never the C# <see cref="Catalog.CatalogAudience"/> enum's default PascalCase
/// serialization), so the Admin UI's 18+ badge (F90.4a) reads the same token the catalog itself uses.
/// </param>
/// <param name="BestFor">Optional genre chips (F90.4a) — empty, never null, when the entry has none.</param>
/// <param name="Preview">
/// A theme entry's shelf-card swatch chips (SPEC F103.4, F103.3) — <see langword="null"/> for every
/// persona and font entry, and for a theme entry whose index carries none. This is the ENTIRE
/// contract a theme shelf card needs to paint chips; the Admin UI fetches nothing further to render
/// one (T185).
/// </param>
public sealed record CatalogShelfEntryDto(
    string Slug, string Kind, string Audience, IReadOnlyList<string> BestFor, CatalogShelfPreviewDto? Preview);
