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
/// anything else on the entry. A font card carries no <see cref="Preview"/> (that stays a theme-only
/// field) — <see cref="FontByteTotal"/>/<see cref="FontFamily"/> are its own kind-specific fields
/// (T194), the same additive shape <see cref="Preview"/> already established for theme.
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
/// <param name="FontByteTotal">
/// A font entry's summed asset bytes (SPEC F104.3's shelf-card "byte total") — <see langword="null"/>
/// for every persona/theme entry. Computed straight off the index's own
/// <see cref="Catalog.CatalogEntrySummary.Assets"/> (T194) — no fetch of any kind, keeping the shelf
/// as cheap for a font card as it already is for every other kind.
/// </param>
/// <param name="FontFamily">
/// A font entry's shelf-card family name (STORY-281 AC1 reconciliation, T194 review finding) —
/// <see langword="null"/> for every persona/theme entry, or a font entry whose index omits/malforms
/// it. UNLIKE <see cref="CatalogEntryResponse.FontFamily"/> (the detail route's own family, parsed
/// from the pack's fetched <c>.font.json</c> manifest at zero EXTRA cost since that route already
/// fetches it), this is sourced straight off <see cref="Catalog.CatalogEntrySummary.Family"/> — the
/// INDEX's own optional field — because this index-level listing never fetches a manifest for every
/// entry just to paint a shelf card (SPEC F90.2's own "metadata and file pointers only" contract).
/// The catalog-side <c>build_index.py</c> projection that actually populates the index's own
/// <c>family</c> field is a later task (T195/T196) — see
/// <see cref="Catalog.CatalogFontManifestSerializer"/>'s own remarks for where that obligation is
/// recorded.
/// </param>
public sealed record CatalogShelfEntryDto(
    string Slug, string Kind, string Audience, IReadOnlyList<string> BestFor, CatalogShelfPreviewDto? Preview,
    long? FontByteTotal, string? FontFamily);
