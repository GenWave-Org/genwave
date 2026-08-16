namespace GenWave.Host.Images;

/// <summary>
/// The two formats <see cref="ImageMagicBytesGate"/> accepts (SPEC F128.6, PLAN T291) — decided by
/// content bytes alone, never by a caller-supplied <c>Content-Type</c> header or file extension.
/// </summary>
internal enum ImageFormat
{
    Png,
    Jpeg,
}
