using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Response body for <c>POST /api/themes/{slug}/import</c> (SPEC F103.6, STORY-272, PLAN T184) — the
/// theme-kind sibling of <see cref="PersonaImportResponse"/>. Narrower than that type: a
/// <c>station.theme</c> row (<see cref="OwnerTheme"/>) carries no numeric id and this route generates
/// no warnings (there is no F79.4-style voice-resolution step for a theme), so neither field is
/// fabricated here — only what <see cref="IThemeStore"/>/the accepted manifest genuinely hand back.
/// </summary>
/// <param name="Slug">The route slug — the upsert key, and what the imported theme now resolves
/// under (<c>ThemeCatalog</c>, <c>Station:Theme</c>).</param>
/// <param name="Name">The manifest's own display name.</param>
/// <param name="ImportedFrom">The provenance stamp actually written: the <c>catalogSlug</c> query
/// value, or <c>"file"</c> for a direct upload (SPEC F103.6/F103.11).</param>
public sealed record ThemeImportResponse(string Slug, string Name, string ImportedFrom);
