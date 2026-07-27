namespace GenWave.Host.Api;

/// <summary>
/// One row on <c>GET /api/catalog/index</c>'s shelf listing (SPEC F90.2) — the wire projection of
/// <see cref="Catalog.CatalogEntrySummary"/>, and ONLY what that type carries: <see cref="Slug"/>,
/// <see cref="Audience"/>, <see cref="BestFor"/>. Tagline/description/author/sample patter are NOT
/// here — those live inside a fetched-and-hash-verified <c>meta.json</c>, which this index-level
/// listing never eagerly fetches for every entry (SPEC F90.2's own "metadata and file pointers
/// only" contract on <see cref="Catalog.CatalogEntrySummary"/>); the Admin UI reads them per-entry
/// via <c>GET /api/catalog/entries/{slug}</c> instead.
/// </summary>
/// <param name="Slug">The catalog entry's own slug (SPEC F90.7's provenance value, once imported).</param>
/// <param name="Audience">
/// <c>"everyone"</c> or <c>"mature"</c> — lowercase, matching genwave-catalog's own schema
/// vocabulary verbatim (never the C# <see cref="Catalog.CatalogAudience"/> enum's default PascalCase
/// serialization), so the Admin UI's 18+ badge (F90.4a) reads the same token the catalog itself uses.
/// </param>
/// <param name="BestFor">Optional genre chips (F90.4a) — empty, never null, when the entry has none.</param>
public sealed record CatalogShelfEntryDto(string Slug, string Audience, IReadOnlyList<string> BestFor);
