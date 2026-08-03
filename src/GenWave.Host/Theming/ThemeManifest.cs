namespace GenWave.Host.Theming;

/// <summary>
/// A theme: a stable <see cref="Slug"/>, display metadata, the fonts it declares, and its
/// light+dark token sets. This is the SAME shape whether GenWave shipped it or an owner authored
/// it (STORY-263 AC4, ARCHITECTURE "Theme system") — no field marks authorship. That single rule
/// is what keeps the future Layer B editor (gh-#206) from being a bolt-on, and what makes a
/// catalog theme a fetch-and-store rather than a second mechanism.
///
/// This is deliberately what a future <c>station.theme.definition jsonb</c> column would hold —
/// keep it serializable and free of runtime-only concerns (no cached CSS, no origin/source flag).
/// Only <see cref="ThemeManifestParser"/> constructs one, and only after every load-time rule in
/// STORY-263 has passed.
///
/// Value equality is genuinely structural: two separately-parsed, field-for-field identical
/// manifests compare <c>Equal</c> and hash identically, which T159's <c>(theme, mode)</c> CSS cache
/// and T164's resolution both rely on. This record needs no <c>Equals</c>/<c>GetHashCode</c>
/// override of its own to get that — the compiler-synthesized equality already delegates to each
/// member's own equality, so it's correct as soon as every member type is. <see cref="ThemeFonts"/>
/// composes only <see cref="ThemeFontFace"/> values (no bare collection), so it needed nothing
/// extra; <see cref="ThemeFontFace"/> and <see cref="ThemeModes"/> each hold a raw collection
/// (<c>IReadOnlyList</c>/<c>IReadOnlyDictionary</c>, which carry no value equality of their own) and
/// so implement their own structural <c>Equals</c>/<c>GetHashCode</c> — see their remarks.
/// </summary>
public sealed record ThemeManifest(
    string Slug,
    string Name,
    string Author,
    ThemeFonts Fonts,
    ThemeModes Modes);
