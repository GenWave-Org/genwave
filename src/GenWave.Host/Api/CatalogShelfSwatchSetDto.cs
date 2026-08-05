using System.Text.Json.Serialization;

namespace GenWave.Host.Api;

/// <summary>
/// The five wire-shape swatches for one mode of a theme entry's shelf preview (SPEC F103.4) —
/// projected verbatim from <see cref="Catalog.CatalogThemeSwatchSet"/>. <see cref="Accent2"/> is
/// serialized as <c>accent-2</c>, matching genwave-catalog's own <c>theme-meta.schema.json</c> key
/// name and the app's <c>ThemeModes</c> token vocabulary — the default camelCase policy this
/// controller's JSON options otherwise use would instead emit <c>accent2</c>.
/// </summary>
public sealed record CatalogShelfSwatchSetDto(
    string Bg,
    string Surface,
    string Ink,
    string Accent,
    [property: JsonPropertyName("accent-2")] string Accent2);
