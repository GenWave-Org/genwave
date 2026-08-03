namespace GenWave.Host.Theming;

/// <summary>
/// One vendored font file backing a <see cref="ThemeFontFace"/>: the asset's source path and the
/// <c>@font-face</c> weight/style it should be declared under. Fonts are assets, not values —
/// <c>ThemeCssComposer</c> (T159) emits <c>@font-face</c> rules straight from these, it never
/// references a face GenWave (or, in Layer B, an owner) didn't vendor.
/// </summary>
public sealed record ThemeFontAsset(string Src, string Weight, string Style);
