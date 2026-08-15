namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// <see cref="ITtsSynthesizer"/> test double for <see cref="CrosstalkAssembler"/> specs (Story327).
/// Unlike <see cref="FakeTtsSynthesizer"/>'s always-zero-sample minimal WAV (fine for
/// <c>TtsSegmentSource</c>'s own file-existence/caching checks, which never care what's actually in
/// the file), <see cref="CrosstalkAssembler"/>'s ffmpeg assembly step genuinely needs a REAL,
/// non-zero-duration audio stream per line to delay/mix — a zero-sample WAV concatenates/mixes to
/// nothing and ffprobe reports it as 0s regardless of what the test wants the "rendered duration" to
/// be. Writes a short, low-amplitude sine tone of a controllable duration per call instead — real
/// bytes ffmpeg can actually delay and mix, small enough that a whole spec suite runs fast.
///
/// Captures every <see cref="TtsRenderContext"/> seen, in call order, so a spec can assert on
/// per-line Rules/Pace/Voice without a second capturing fake (mirrors
/// <see cref="FakeTtsSynthesizer.LastContext"/>'s own capture, widened to ALL calls since
/// <see cref="CrosstalkAssembler"/> renders several lines per exchange, not one segment per
/// request).
/// </summary>
public sealed class FakeCrosstalkVoiceSynthesizer : ITtsSynthesizer
{
    /// <summary>Every context this fake has rendered, in call order (0-based — line 0 is
    /// <c>Contexts[0]</c>).</summary>
    public List<TtsRenderContext> Contexts { get; } = [];

    /// <summary>How long each rendered tone is, in seconds. 0.3s by default — long enough for
    /// ffprobe to report a stable, non-zero duration, short enough that an 8-line exchange still
    /// renders/mixes in well under a second of wall-clock test time.</summary>
    public double LineDurationSeconds { get; set; } = 0.3;

    /// <summary>Peak amplitude of every rendered tone, as a fraction of full scale (0.0-1.0). A
    /// quiet 0.2 by default (plenty for ffprobe/silencedetect-style checks); a headroom/clipping
    /// spec (T284 review F6) sets this to 1.0 — genuinely full-scale — to exercise the worst case an
    /// interjection's overlap can actually produce.</summary>
    public double Amplitude { get; set; } = 0.2;

    /// <summary>1-based call number (the Nth line rendered across the whole exchange) this fake
    /// throws on, when set — models SPEC F99's right-voice bar failing partway through an exchange.
    /// Null (the default) never throws.</summary>
    public int? ThrowOnCallNumber { get; set; }

    /// <summary>Directory synthesized files are written under. Defaults to a fresh temp directory
    /// per fake instance so parallel specs never collide.</summary>
    public string OutputDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    /// <summary>Every path this fake has written so far, in call order — lets a spec assert a
    /// discarded exchange left none of them behind (SPEC F127.5's "no asset behind" sad path).</summary>
    public List<string> WrittenPaths { get; } = [];

    int callCount;

    public Task<string> SynthesizeAsync(TtsRenderContext context, CancellationToken ct) =>
        RenderAsync(context, ct);

    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct) =>
        RenderAsync(new TtsRenderContext(text, voice, Kind: null), ct);

    async Task<string> RenderAsync(TtsRenderContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        callCount++;
        Contexts.Add(context);

        if (ThrowOnCallNumber == callCount)
            throw new InvalidOperationException($"FakeCrosstalkVoiceSynthesizer: simulated failure on call {callCount}");

        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(path, CreateToneWav(LineDurationSeconds, Amplitude), ct);
        WrittenPaths.Add(path);
        return path;
    }

    /// <summary>A real mono 16-bit PCM WAV of <paramref name="seconds"/> at a 440 Hz tone, peaking at
    /// <paramref name="amplitudeFraction"/> of full scale — real, non-silent samples so ffmpeg's own
    /// duration probing and mixing behave exactly as they would on genuine speech audio, without
    /// needing a real TTS engine in the test suite.</summary>
    static byte[] CreateToneWav(double seconds, double amplitudeFraction, double frequencyHz = 440.0)
    {
        const int sampleRate = 44100;
        const short bitsPerSample = 16;
        const short channels = 1;
        var amplitude = amplitudeFraction * short.MaxValue;

        var sampleCount = Math.Max(1, (int)Math.Round(seconds * sampleRate));
        var dataSize = sampleCount * channels * (bitsPerSample / 8);
        var bytes = new byte[44 + dataSize];

        bytes[0] = (byte)'R'; bytes[1] = (byte)'I'; bytes[2] = (byte)'F'; bytes[3] = (byte)'F';
        WriteInt32LE(bytes, 4, 36 + dataSize);
        bytes[8] = (byte)'W'; bytes[9] = (byte)'A'; bytes[10] = (byte)'V'; bytes[11] = (byte)'E';
        bytes[12] = (byte)'f'; bytes[13] = (byte)'m'; bytes[14] = (byte)'t'; bytes[15] = (byte)' ';
        WriteInt32LE(bytes, 16, 16);
        WriteInt16LE(bytes, 20, 1);
        WriteInt16LE(bytes, 22, channels);
        WriteInt32LE(bytes, 24, sampleRate);
        WriteInt32LE(bytes, 28, sampleRate * channels * (bitsPerSample / 8));
        WriteInt16LE(bytes, 32, (short)(channels * (bitsPerSample / 8)));
        WriteInt16LE(bytes, 34, bitsPerSample);
        bytes[36] = (byte)'d'; bytes[37] = (byte)'a'; bytes[38] = (byte)'t'; bytes[39] = (byte)'a';
        WriteInt32LE(bytes, 40, dataSize);

        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(amplitude * Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate));
            WriteInt16LE(bytes, 44 + (i * 2), sample);
        }

        return bytes;
    }

    static void WriteInt32LE(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WriteInt16LE(byte[] buf, int offset, short value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}
