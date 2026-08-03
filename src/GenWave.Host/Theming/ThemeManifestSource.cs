namespace GenWave.Host.Theming;

/// <summary>
/// One raw, not-yet-trusted theme manifest document plus an origin label — an embedded resource
/// name in production, a fixture name in tests — used only for error messages when parsing fails
/// before the manifest's own <c>slug</c> can be read.
/// </summary>
public sealed record ThemeManifestSource(string Name, string Json);
