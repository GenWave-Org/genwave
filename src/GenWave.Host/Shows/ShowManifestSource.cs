namespace GenWave.Host.Shows;

/// <summary>
/// One raw, not-yet-trusted show import manifest document plus an origin label — mirrors
/// <see cref="Theming.ThemeManifestSource"/>; used only for error messages when parsing fails before
/// the manifest can be trusted at all.
/// </summary>
public sealed record ShowManifestSource(string Name, string Json);
