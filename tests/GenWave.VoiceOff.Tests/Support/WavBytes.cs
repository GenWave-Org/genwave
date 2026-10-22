namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Builds small WAV byte arrays for header-reading facts.</summary>
internal static class WavBytes
{
    /// <summary>A 16-bit mono WAV of <paramref name="dataBytes"/> bytes at <paramref name="sampleRate"/>; <paramref name="declaredData"/> overrides the data size the header claims.</summary>
    internal static byte[] Make(int sampleRate, int dataBytes, uint? declaredData = null, bool withFmt = true, string riff = "RIFF", string extraChunk = "", string form = "WAVE", int fmtSize = 16)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(riff));
        writer.Write(36 + dataBytes);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(form));
        if (extraChunk.Length > 0)
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes(extraChunk));
            writer.Write(3);
            writer.Write(new byte[] { 1, 2, 3, 0 });
        }

        if (withFmt)
        {
            using MemoryStream body = new();
            using BinaryWriter fmt = new(body);
            fmt.Write((short)1);
            fmt.Write((short)1);
            fmt.Write(sampleRate);
            fmt.Write(sampleRate * 2);
            fmt.Write((short)2);
            fmt.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(fmtSize);
            // A chunk that declares fewer bytes than the usual 16 really has only that many.
            writer.Write(body.ToArray()[..Math.Min(fmtSize, 16)]);
        }

        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(declaredData ?? (uint)dataBytes);
        writer.Write(new byte[dataBytes]);
        return stream.ToArray();
    }

    internal static byte[] Truncate(byte[] bytes, int length) => bytes[..length];
}
