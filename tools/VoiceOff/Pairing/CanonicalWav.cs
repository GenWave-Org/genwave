using System.Buffers.Binary;
using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Pairing;

/// <summary>
/// Re-writes a PCM WAV as the plainest RIFF there is: a 44-byte header (RIFF, WAVE, fmt, data) and the sample bytes. Every other chunk
/// (LIST/INFO tags, cue points, anything an engine or encoder added) is dropped, and a streaming placeholder data size becomes the real size.
/// Pure: no ffmpeg, no files.
/// </summary>
internal static class CanonicalWav
{
    internal const int HeaderBytes = 44;
    private const int ChunkHeaderBytes = 8;
    private const int FmtBodyBytes = 16;
    private const ushort PcmFormat = 1;
    private const uint StreamingSize = uint.MaxValue;

    /// <summary>The canonical bytes, or null with <paramref name="problem"/> saying why the input is not a usable PCM WAV.</summary>
    internal static byte[]? Rewrite(ReadOnlySpan<byte> bytes, out string? problem)
    {
        if (!RiffChunkReader.IsWave(bytes))
        {
            problem = "the audio is not a RIFF/WAVE file";
            return null;
        }

        ReadOnlySpan<byte> fmt = default;
        RiffChunkReader chunks = new(bytes);
        while (chunks.TryNext(out ReadOnlySpan<byte> id, out long size, out int body))
        {
            if (id.SequenceEqual("fmt "u8))
            {
                if (size < FmtBodyBytes || body + FmtBodyBytes > bytes.Length)
                {
                    problem = "the WAV fmt chunk is cut short";
                    return null;
                }

                fmt = bytes.Slice(body, FmtBodyBytes);
            }
            else if (id.SequenceEqual("data"u8))
            {
                return Build(fmt, bytes[body..], size, out problem);
            }
        }

        problem = "the WAV has no data chunk";
        return null;
    }

    private static byte[]? Build(ReadOnlySpan<byte> fmt, ReadOnlySpan<byte> rest, long declared, out string? problem)
    {
        if (fmt.IsEmpty)
        {
            problem = "the WAV has no fmt chunk before its data";
            return null;
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(fmt) != PcmFormat)
        {
            problem = "the WAV is not plain PCM";
            return null;
        }

        long size = declared == StreamingSize ? rest.Length : declared;
        if (size > rest.Length)
        {
            problem = $"the WAV is truncated: its header promises {size} data bytes and {rest.Length} arrived";
            return null;
        }

        int pad = (int)(size & 1);
        byte[] output = new byte[HeaderBytes + size + pad];
        Span<byte> o = output;
        "RIFF"u8.CopyTo(o);
        BinaryPrimitives.WriteUInt32LittleEndian(o[4..], (uint)(HeaderBytes - ChunkHeaderBytes + size + pad));
        "WAVEfmt "u8.CopyTo(o[8..]);
        BinaryPrimitives.WriteUInt32LittleEndian(o[16..], FmtBodyBytes);
        fmt.CopyTo(o[20..]);
        "data"u8.CopyTo(o[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(o[40..], (uint)size);
        rest[..(int)size].CopyTo(o[HeaderBytes..]);
        problem = null;
        return output;
    }
}
