namespace GenWave.Host.Api;

/// <summary>
/// A theme entry's shelf-card preview, on the wire (SPEC F103.4) — projected from
/// <see cref="Catalog.CatalogThemePreview"/>. Only ever present on <see cref="CatalogShelfEntryDto"/>
/// when <see cref="Catalog.CatalogEntrySummary.Preview"/> resolved non-null; the Admin UI paints
/// swatch chips straight off this, never fetching or composing a theme's manifest to do so.
/// </summary>
public sealed record CatalogShelfPreviewDto(CatalogShelfSwatchSetDto Light, CatalogShelfSwatchSetDto Dark);
