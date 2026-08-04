namespace GenWave.Host.Api;

/// <summary>
/// One selectable entry in <c>GET /spectator/api/themes</c>' <c>options</c> array (SPEC F102.10a,
/// STORY-266, PLAN T166): just enough for the switcher's <c>&lt;select&gt;</c> to populate itself
/// — a value to persist and a label to display, nothing from <see cref="Theming.ThemeManifest"/>'s
/// fonts/tokens (those already ride <c>theme.css</c>, never this JSON payload).
/// </summary>
/// <param name="Slug">The theme's stable identifier — what the switcher writes into the
/// <c>genwave-theme</c> cookie (<see cref="Theming.ThemeCatalog.CookieName"/>).</param>
/// <param name="Name">The theme's display name.</param>
public sealed record SpectatorThemeOption(string Slug, string Name);
