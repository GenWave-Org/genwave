namespace GenWave.Host.Theming;

/// <summary>
/// One <see cref="ThemeCatalog"/> entry — a loaded <see cref="ThemeManifest"/> paired with its
/// <see cref="ThemeProvenance"/>, if any (SPEC F103.11, PLAN T187; review F3). The shape
/// <see cref="Configuration.StationSettingsAllowlist.ThemeChoices"/> walks directly (via
/// <see cref="ThemeCatalog.Entries"/>) so stamping each <see cref="Configuration.SettingChoice"/>'s
/// provenance never needs a second, per-item lookup back into the catalog for a slug
/// <see cref="ThemeCatalog.All"/> already named — <see cref="ThemeCatalog"/> already knows, while it
/// is building this list, which owner row (if any) each slug loaded from.
/// </summary>
/// <param name="Theme">The loaded, validated manifest — the exact same value <see cref="ThemeCatalog.All"/>
/// and <see cref="ThemeCatalog.TryGetBySlug"/> serve.</param>
/// <param name="Provenance"><see langword="null"/> for a shipped default (or any slug this catalog
/// has no owner row for); populated for a catalog- or file-imported theme.</param>
internal sealed record ThemeCatalogEntry(ThemeManifest Theme, ThemeProvenance? Provenance);
