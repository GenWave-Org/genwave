namespace GenWave.Host.Images;

/// <summary>
/// The magic-bytes gate (SPEC F128.6, PLAN T291): identifies PNG/JPEG by content signature alone —
/// never by the caller's <c>Content-Type</c> header or any file extension — and runs BEFORE any
/// decoder (ffmpeg included) ever sees the bytes.
/// </summary>
internal static class ImageMagicBytesGate
{
    /// <summary>The detected format, or <see langword="null"/> when neither signature matches.</summary>
    public static ImageFormat? Detect(ReadOnlySpan<byte> bytes)
    {
        if (PngImageHeader.HasSignature(bytes))
            return ImageFormat.Png;
        if (JpegImageHeader.HasSoiMarker(bytes))
            return ImageFormat.Jpeg;
        return null;
    }
}
