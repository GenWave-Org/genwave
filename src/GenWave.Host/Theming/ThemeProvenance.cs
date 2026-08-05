namespace GenWave.Host.Theming;

/// <summary>
/// Minimal owner-theme provenance carrier (SPEC F103.11, PLAN T187; PLAN T187 review F3) — just the
/// two scalars <see cref="Configuration.SettingChoice.ImportedFrom"/>/
/// <see cref="Configuration.SettingChoice.ImportedAt"/> ever read off a <c>station.theme</c> row, so
/// <see cref="ThemeCatalog"/> never has to retain a whole <see cref="GenWave.Core.Domain.OwnerTheme"/>
/// — its <see cref="GenWave.Core.Domain.OwnerTheme.Definition"/> jsonb is already re-parsed into a
/// <see cref="ThemeManifest"/> at load time, so keeping the raw row around too would be dead weight
/// carried only to answer two field reads later. Attached to a <see cref="ThemeCatalogEntry"/> only
/// when the theme actually loaded from an owner row — never for a shipped default.
/// </summary>
/// <param name="ImportedFrom">Same meaning as <see cref="GenWave.Core.Domain.OwnerTheme.ImportedFrom"/>:
/// the catalog entry's slug for a catalog import, <c>"file"</c> for a direct upload, or
/// <see langword="null"/> for an authored-in-place theme (no writer for that path exists yet).</param>
/// <param name="ImportedAt">Same meaning as <see cref="GenWave.Core.Domain.OwnerTheme.ImportedAt"/>;
/// <see langword="null"/> exactly when <paramref name="ImportedFrom"/> is.</param>
internal sealed record ThemeProvenance(string? ImportedFrom, DateTime? ImportedAt);
