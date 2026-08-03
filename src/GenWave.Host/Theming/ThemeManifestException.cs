namespace GenWave.Host.Theming;

/// <summary>
/// A shipped theme manifest failed load-time validation (STORY-263). The message always names the
/// offending theme and, where the failure is mode- or token-scoped, the mode and the token too —
/// "invalid theme" alone is never enough to act on. Thrown by <see cref="ThemeManifestParser"/>
/// and <see cref="ThemeCatalog"/>; a bad shipped theme is a build-time authoring bug, not a
/// request-time condition a caller should branch around, so this is an exception rather than a
/// <c>Result</c>.
/// </summary>
public sealed class ThemeManifestException : Exception
{
    public ThemeManifestException(string message)
        : base(message)
    {
    }
}
