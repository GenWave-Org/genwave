namespace GenWave.Host.Theming;

/// <summary>
/// A theme's two required font roles (ARCHITECTURE "Theme system") — <see cref="Display"/> for
/// headings/wordmark, <see cref="Sans"/> for body copy. Both are mandatory: a theme with only one
/// declared face is rejected at load, the same way an incomplete <see cref="ThemeModes"/> is.
/// </summary>
public sealed record ThemeFonts(ThemeFontFace Display, ThemeFontFace Sans);
