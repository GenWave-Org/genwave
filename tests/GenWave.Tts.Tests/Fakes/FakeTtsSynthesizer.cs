namespace GenWave.Tts.Tests.Fakes;

using System.Security.Cryptography;
using System.Text;
using GenWave.Core.Abstractions;

public sealed class FakeTtsSynthesizer : ITtsSynthesizer
{
    public int CallCount { get; private set; }
    public string? LastReturnedPath { get; private set; }
    public string? LastText { get; private set; }
    public string? LastVoice { get; private set; }

    /// <summary>When non-null, the next call to SynthesizeAsync will throw this exception.</summary>
    public Exception? ThrowOnNextCall { get; set; }

    /// <summary>Directory where synthesized files are written. Defaults to a temp directory.</summary>
    public string OutputDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void ResetCallCount() => CallCount = 0;

    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LastText = text;
        LastVoice = voice;

        if (ThrowOnNextCall is { } ex)
        {
            ThrowOnNextCall = null;
            throw ex;
        }

        CallCount++;

        Directory.CreateDirectory(OutputDirectory);

        // Use the same hash formula as KokoroTtsSynthesizer so that when OutputDirectory
        // matches TtsOptions.CacheRoot the cache-hit check in TtsSegmentSource succeeds.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text + "|" + voice)));
        var path = Path.Combine(OutputDirectory, $"{hash}.wav");
        File.WriteAllBytes(path, CreateMinimalWav());

        LastReturnedPath = path;
        return Task.FromResult(path);
    }

    static byte[] CreateMinimalWav()
    {
        // Minimal valid WAV: RIFF header for a 0-sample, mono, 16-bit, 44100 Hz file.
        var bytes = new byte[44];
        // RIFF chunk descriptor
        bytes[0] = (byte)'R'; bytes[1] = (byte)'I'; bytes[2] = (byte)'F'; bytes[3] = (byte)'F';
        WriteInt32LE(bytes, 4, 36);         // ChunkSize: 36 + data size (0)
        bytes[8] = (byte)'W'; bytes[9] = (byte)'A'; bytes[10] = (byte)'V'; bytes[11] = (byte)'E';
        // fmt sub-chunk
        bytes[12] = (byte)'f'; bytes[13] = (byte)'m'; bytes[14] = (byte)'t'; bytes[15] = (byte)' ';
        WriteInt32LE(bytes, 16, 16);        // Subchunk1Size: 16 for PCM
        WriteInt16LE(bytes, 20, 1);         // AudioFormat: 1 = PCM
        WriteInt16LE(bytes, 22, 1);         // NumChannels: 1
        WriteInt32LE(bytes, 24, 44100);     // SampleRate
        WriteInt32LE(bytes, 28, 88200);     // ByteRate
        WriteInt16LE(bytes, 32, 2);         // BlockAlign
        WriteInt16LE(bytes, 34, 16);        // BitsPerSample
        // data sub-chunk
        bytes[36] = (byte)'d'; bytes[37] = (byte)'a'; bytes[38] = (byte)'t'; bytes[39] = (byte)'a';
        WriteInt32LE(bytes, 40, 0);         // Subchunk2Size: 0 samples
        return bytes;
    }

    static void WriteInt32LE(byte[] buf, int offset, int value)
    {
        buf[offset]     = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8)  & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WriteInt16LE(byte[] buf, int offset, short value)
    {
        buf[offset]     = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}
