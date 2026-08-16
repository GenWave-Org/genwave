using System.Buffers.Binary;

namespace GenWave.Host.Images;

/// <summary>
/// Byte-level PNG parsing for the SPEC F128.6 upload gates (<see cref="ImageNormalizeService"/>) —
/// signature check, IHDR dimensions, and acTL (APNG) detection — all BEFORE any decoder ever
/// touches the bytes; the decompression-bomb class dies here, not inside ffmpeg. Every method is a
/// bounds-checked span walk over the raw chunk stream: a truncated or malformed chunk sequence
/// returns <see langword="false"/> rather than throwing, since a hostile or merely corrupt upload
/// is exactly the input this class exists to survive.
/// </summary>
internal static class PngImageHeader
{
    static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool HasSignature(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= Signature.Length && bytes[..Signature.Length].SequenceEqual(Signature);

    /// <summary>
    /// Reads width/height straight from the IHDR chunk, which the PNG spec requires to be the
    /// FIRST chunk immediately after the 8-byte signature — never trusts an IHDR-shaped chunk
    /// found later in the stream. <see langword="false"/> on anything shorter or differently
    /// shaped than that fixed layout.
    /// </summary>
    public static bool TryReadDimensions(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        // signature(8) + length(4) + type(4) + width(4) + height(4) = 24 bytes minimum.
        if (bytes.Length < 24 || !HasSignature(bytes))
            return false;

        if (!bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            return false;

        width = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        return true;
    }

    /// <summary>
    /// True when an <c>acTL</c> (animation control) chunk appears before the first <c>IDAT</c> —
    /// the APNG spec's own definition of "this PNG is animated" (SPEC F128.1's catalog-CI rule,
    /// mirrored here for the upload path). Walks the chunk stream length-prefixed chunk by chunk;
    /// a truncated or malformed walk stops and reports <see langword="false"/> rather than
    /// throwing — a corrupt tail past a well-formed IHDR is <see cref="ImageNormalizeService"/>'s
    /// ffmpeg stage's problem to reject, not this scan's.
    /// </summary>
    public static bool HasAnimationChunk(ReadOnlySpan<byte> bytes)
    {
        if (!HasSignature(bytes))
            return false;

        var offset = Signature.Length;
        while (offset + 8 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            var type = bytes.Slice(offset + 4, 4);

            if (type.SequenceEqual("acTL"u8))
                return true;
            if (type.SequenceEqual("IDAT"u8))
                return false;

            // chunk header(8) + data(length) + crc(4) — an overflow-shaped or out-of-range length
            // ends the walk (malformed chunk) instead of wrapping into a negative/runaway advance.
            var next = (long)offset + 8 + length + 4;
            if (length > int.MaxValue - 12 || next > bytes.Length)
                return false;

            offset = (int)next;
        }

        return false;
    }
}
