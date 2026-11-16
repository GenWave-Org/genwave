using GenWave.Core.Abstractions;

namespace GenWave.Host.Tests.Support;

/// <summary>An <see cref="ITtsSynthesizer"/> that renders half a second of real (silent) PCM so ffmpeg can mix and probe it.</summary>
internal sealed class SilentWavSynthesizer : ITtsSynthesizer
{
    const int SampleRate = 22050;
    const int SampleCount = SampleRate / 2;

    public async Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(path, Wav(), ct);
        return path;
    }

    static byte[] Wav()
    {
        const int dataSize = SampleCount * 2;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        writer.Flush();
        return stream.ToArray();
    }
}
