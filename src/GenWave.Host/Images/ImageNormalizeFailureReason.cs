namespace GenWave.Host.Images;

/// <summary>
/// Every quiet-400 reason <see cref="ImageNormalizeService.NormalizeAsync"/> can fail with (SPEC
/// F128.6, PLAN T291) — ordered the same as the gates run: byte length, magic bytes, header
/// dimensions/APNG, then the ffmpeg re-encode itself. Carries no message text of its own (a reason
/// enum, not a string) — the HTTP mapping owns its own user-facing copy, per T295/T307's own
/// ProblemDetails controllers.
/// </summary>
public enum ImageNormalizeFailureReason
{
    /// <summary>The body is zero bytes — distinct from <see cref="TooLarge"/> so an empty upload
    /// never reads as an oversize one in logs or the T295/T307 ProblemDetails mapping.</summary>
    Empty,

    /// <summary>The body exceeds <see cref="ImageNormalizeService.MaxInputBytes"/>.</summary>
    TooLarge,

    /// <summary>Neither the PNG signature nor a JPEG SOI marker stream matched, or the matched
    /// format's own header could not be parsed for dimensions.</summary>
    NotAnImage,

    /// <summary>A PNG carrying an <c>acTL</c> chunk (APNG) — an animated face must never slip in
    /// via upload (SPEC F128.1).</summary>
    Animated,

    /// <summary>Width or height read under 256px.</summary>
    DimensionsTooSmall,

    /// <summary>Width or height read over 4096px — the decompression-bomb class.</summary>
    DimensionsTooLarge,

    /// <summary>The ffmpeg re-encode itself failed (missing/unusable binary, non-zero exit, no
    /// usable PNG on disk after it returned) or exceeded its bounded runtime, or its output
    /// exceeded <see cref="ImageNormalizeService.MaxOutputBytes"/>.</summary>
    EncodeFailed,
}
