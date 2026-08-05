namespace GenWave.Host.Catalog;

/// <summary>
/// The five shelf-chip swatches for one mode of a theme entry's preview (SPEC F103.4, T185's
/// contract, catalog-owned <c>theme-meta.schema.json</c>'s <c>preview.light</c>/<c>preview.dark</c>
/// shape). Names mirror a subset of the app's own <c>ThemeModes</c> token vocabulary — <c>Accent2</c>
/// carries the wire name <c>accent-2</c> (see <see cref="CatalogIndexValidator"/>'s ephemeral
/// projection, the only place that JSON key name is spelled). This is a display-only projection:
/// nothing here is re-validated against a theme's real manifest tokens, and it is never authoritative
/// once a theme is installed — see the catalog schema's own remarks.
/// </summary>
public sealed record CatalogThemeSwatchSet(string Bg, string Surface, string Ink, string Accent, string Accent2);
