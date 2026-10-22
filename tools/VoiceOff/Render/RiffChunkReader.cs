using System.Buffers.Binary;

namespace GenWave.VoiceOff.Render;

/// <summary>
/// Walks the chunks of a RIFF/WAVE file. Offsets are <c>long</c> and the walk stops when the next chunk header would not fit, so a chunk
/// that declares a huge size (0xFFFFFFFF) ends the walk instead of wrapping an offset. Never throws on malformed bytes.
/// </summary>
internal ref struct RiffChunkReader
{
    private const int RiffHeaderBytes = 12;
    private const int ChunkHeaderBytes = 8;

    private readonly ReadOnlySpan<byte> bytes;
    private long position;

    internal RiffChunkReader(ReadOnlySpan<byte> bytes)
    {
        this.bytes = bytes;
        position = RiffHeaderBytes;
    }

    /// <summary>True when the bytes start with a RIFF/WAVE header.</summary>
    internal static bool IsWave(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= RiffHeaderBytes && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WAVE"u8);

    /// <summary>The next chunk: its id, its declared size, and where its body starts. False when no further header fits.</summary>
    internal bool TryNext(out ReadOnlySpan<byte> id, out long declaredSize, out int bodyOffset)
    {
        if (position + ChunkHeaderBytes > bytes.Length)
        {
            id = default;
            declaredSize = 0;
            bodyOffset = 0;
            return false;
        }

        int header = (int)position;
        id = bytes.Slice(header, 4);
        declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(header + 4, 4));
        bodyOffset = header + ChunkHeaderBytes;
        position = bodyOffset + declaredSize + (declaredSize & 1);
        return true;
    }
}
