using System.Buffers.Binary;

namespace GenWave.Host.Images;

/// <summary>
/// Byte-level JPEG parsing for the SPEC F128.6 upload gates (<see cref="ImageNormalizeService"/>) —
/// SOI + marker-structure validation and an SOF0/SOF2 dimension read, both BEFORE any decoder runs.
/// A bounds-checked marker-segment walk that tolerates the JPEG spec's own 0xFF fill-byte padding
/// between markers; a truncated or malformed stream returns <see langword="false"/> rather than
/// throwing, mirroring <see cref="PngImageHeader"/>'s own fail-closed posture for a hostile input.
/// </summary>
internal static class JpegImageHeader
{
    const byte MarkerPrefix = 0xFF;
    const byte Soi = 0xD8;
    const byte Sof0 = 0xC0; // baseline DCT
    const byte Sof2 = 0xC2; // progressive DCT
    const byte Eoi = 0xD9;

    /// <summary>
    /// True when the bytes open with the SOI marker (FF D8) immediately followed by another
    /// marker's own FF prefix — proves this is a real marker STREAM, not merely two bytes that
    /// happen to match SOI, without yet walking the full segment chain.
    /// </summary>
    public static bool HasSoiMarker(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == MarkerPrefix && bytes[1] == Soi && bytes[2] == MarkerPrefix;

    /// <summary>
    /// Scans the marker segment stream for SOF0 (baseline) or SOF2 (progressive) and reads its
    /// height/width fields. <see langword="false"/> if neither is found before EOI/truncation, or
    /// the stream loses sync — never trusts a segment length that would run past the end of
    /// <paramref name="bytes"/>.
    /// </summary>
    public static bool TryReadDimensions(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (!HasSoiMarker(bytes))
            return false;

        var offset = 2;
        while (offset < bytes.Length)
        {
            if (bytes[offset] != MarkerPrefix)
                return false; // lost sync with the marker stream

            offset++;
            // The JPEG spec permits any number of 0xFF fill bytes before the real marker code.
            while (offset < bytes.Length && bytes[offset] == MarkerPrefix)
                offset++;

            if (offset >= bytes.Length)
                return false;

            var marker = bytes[offset];
            offset++;

            if (marker == Eoi)
                return false;

            // Standalone markers carry no length/payload: RST0-RST7 (D0-D7), TEM (01).
            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
                continue;

            if (offset + 2 > bytes.Length)
                return false;

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (segmentLength < 2)
                return false;

            if (marker is Sof0 or Sof2)
            {
                // length(2) + precision(1) + height(2) + width(2) = 7 bytes from `offset`.
                if (offset + 7 > bytes.Length)
                    return false;

                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                return true;
            }

            var next = offset + segmentLength;
            if (next <= offset || next > bytes.Length)
                return false;

            offset = next;
        }

        return false;
    }
}
