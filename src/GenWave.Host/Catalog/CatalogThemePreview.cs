namespace GenWave.Host.Catalog;

/// <summary>
/// A theme entry's shelf-card preview (SPEC F103.4, T185's contract) — light and dark swatch sets
/// cheap enough for <see cref="CatalogIndexValidator"/> to admit straight off an index.json entry
/// (projected there by genwave-catalog's own <c>build_index.py</c> from the entry's
/// <c>meta.json</c>, T191), so the shelf never fetches or parses a theme's actual manifest just to
/// paint a card. <see langword="null"/> on <see cref="CatalogEntrySummary.Preview"/> is not an error
/// — an entry from an index built before this field existed simply renders no chips.
/// </summary>
public sealed record CatalogThemePreview(CatalogThemeSwatchSet Light, CatalogThemeSwatchSet Dark);
