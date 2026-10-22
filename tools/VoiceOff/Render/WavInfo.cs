using System.Buffers.Binary;

namespace GenWave.VoiceOff.Render;

/// <summary>How long a WAV is, read from its header: the <c>data</c> chunk's bytes over the <c>fmt </c> chunk's byte rate.</summary>
internal sealed record WavInfo(double DurationMs)
{
    private const int RiffHeaderBytes = 12;
    private const int ChunkHeaderBytes = 8;
    private const int ByteRateOffset = 8;
    private const int ByteRateEnd = 12;

    // A streaming engine cannot know the length up front, so it writes this placeholder and the data runs to the end of the body.
    private const uint StreamingSize = uint.MaxValue;

    /// <summary>The info, or null with <paramref name="problem"/> saying why the bytes are not a usable WAV.</summary>
    internal static WavInfo? Read(ReadOnlySpan<byte> bytes, out string? problem)
    {
        if (bytes.Length < RiffHeaderBytes
            || !bytes[..4].SequenceEqual("RIFF"u8)
            || !bytes[8..12].SequenceEqual("WAVE"u8))
        {
            problem = "the audio is not a RIFF/WAVE file";
            return null;
        }

        uint byteRate = 0;
        int position = RiffHeaderBytes;
        while (position + ChunkHeaderBytes <= bytes.Length)
        {
            ReadOnlySpan<byte> id = bytes.Slice(position, 4);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 4, 4));
            int body = position + ChunkHeaderBytes;
            if (id.SequenceEqual("fmt "u8))
            {
                if (size < ByteRateEnd || body + ByteRateEnd > bytes.Length)
                {
                    problem = "the WAV fmt chunk is cut short";
                    return null;
                }

                byteRate = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(body + ByteRateOffset, 4));
            }
            else if (id.SequenceEqual("data"u8))
            {
                return FromData(size, bytes.Length - body, byteRate, out problem);
            }

            position = (int)Math.Min(int.MaxValue, body + size + (size & 1));
        }

        problem = "the WAV has no data chunk";
        return null;
    }

    private static WavInfo? FromData(long size, long available, uint byteRate, out string? problem)
    {
        if (byteRate == 0)
        {
            problem = "the WAV has no readable fmt chunk before its data";
            return null;
        }

        if (size == StreamingSize)
        {
            size = available;
        }

        if (size > available)
        {
            problem = $"the WAV is truncated: its header promises {size} data bytes and {available} arrived";
            return null;
        }

        problem = null;
        return new WavInfo(size * 1000d / byteRate);
    }
}
