using System.Diagnostics;
using System.Globalization;

namespace GenWave.Host.Tests;

/// <summary>
/// Generates small real audio files, via the real ffmpeg binary, for the jingle-pack install specs
/// (SPEC F165, STORY-399/401, PLAN T414). Mirrors <c>GenWave.MediaLibrary.Tests.TestMedia</c>'s own
/// "exercise the real binary, don't fake the bytes" idiom (and <see cref="TestImages"/>'s own local
/// copy of it) — <c>GenWave.Host.Tests</c> has no project reference to
/// <c>GenWave.MediaLibrary.Tests</c> (test projects don't reference each other here), so this is its
/// own small copy rather than a shared one.
/// </summary>
static class JingleTestAudio
{
    /// <summary>
    /// A WAV with <paramref name="leadingSilenceSec"/> of silence, a <paramref name="toneSec"/> 1 kHz
    /// tone, then <paramref name="trailingSilenceSec"/> of silence — real, measurable loudness, a
    /// real analyzer-detected cue narrower than the file's own true start/end, and a real TagLib-
    /// readable duration. Used to prove <c>JingleCuePolicy.Apply</c>'s own per-role decision actually
    /// reaches the row a <c>bed</c>/<c>sting</c>/<c>station_id</c> install writes: a <c>bed</c> or
    /// <c>sting</c> stores this file's own true 0..<c>TotalDurationSec</c> span even though the
    /// analyzer itself would trim the silence; a <c>station_id</c> stores the analyzer's own
    /// narrower, silence-trimmed span.
    /// </summary>
    public static string CreateBedShapedTone(
        string dir, string fileName,
        double leadingSilenceSec = 1.0, double toneSec = 3.0, double trailingSilenceSec = 1.0)
    {
        var path = Path.Combine(dir, fileName);
        var lead = leadingSilenceSec.ToString(CultureInfo.InvariantCulture);
        var tone = toneSec.ToString(CultureInfo.InvariantCulture);
        var trail = trailingSilenceSec.ToString(CultureInfo.InvariantCulture);
        var args = new List<string>
        {
            "-nostats", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"aevalsrc=0:d={lead}",
            "-f", "lavfi", "-i", $"sine=frequency=1000:duration={tone}",
            "-f", "lavfi", "-i", $"aevalsrc=0:d={trail}",
            "-filter_complex", "[0:a][1:a][2:a]concat=n=3:v=0:a=1[out]",
            "-map", "[out]",
            "-ar", "44100", "-ac", "2",
            path,
        };
        RunFfmpeg(args);
        return path;
    }

    /// <summary>A plain, silence-free WAV tone of exactly <paramref name="seconds"/> — the ordinary
    /// case for a manifest asset that just needs to decode, measure, and land cleanly.</summary>
    public static string CreateTone(string dir, string fileName, double seconds = 2.0, int frequency = 440)
    {
        var path = Path.Combine(dir, fileName);
        var args = new List<string>
        {
            "-nostats", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"sine=frequency={frequency}:duration={seconds.ToString(CultureInfo.InvariantCulture)}",
            "-ar", "44100", "-ac", "2",
            path,
        };
        RunFfmpeg(args);
        return path;
    }

    /// <summary>A file with a media extension but non-audio bytes — install must refuse it (SPEC
    /// F165.5's "no half-installed pack"), not crash.</summary>
    public static string CreateCorrupt(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, "this is definitely not audio data"u8.ToArray());
        return path;
    }

    /// <summary>
    /// Real FLAC-encoded bytes under a <c>.wav</c> file name (T414 delta review finding 2) — ffmpeg's
    /// own probe reads the CONTENT, so loudness stays fully measurable, but <see cref="TagLib.File.Create(string)"/>
    /// resolves its parser from the file's EXTENSION and hands FLAC bytes to the RIFF/WAV parser, which
    /// throws <c>TagLib.CorruptFileException</c> ("File does not begin with RIFF identifier") — verified
    /// by hand: <c>ffmpeg -i x.wav -filter_complex ebur128=peak=true -f null -</c> reports a real
    /// integrated LUFS + finite peak; a standalone TagLib read throws exactly that exception. The
    /// forced <c>-f flac</c> muxer is required — ffmpeg otherwise infers the container from the
    /// <c>.wav</c> extension and writes a genuine RIFF/WAV container regardless of the codec.
    /// </summary>
    public static string CreateFlacNamedAsWav(string dir, string fileName, double seconds = 2.0, int frequency = 440)
    {
        var path = Path.Combine(dir, fileName);
        var args = new List<string>
        {
            "-nostats", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"sine=frequency={frequency}:duration={seconds.ToString(CultureInfo.InvariantCulture)}",
            "-c:a", "flac", "-f", "flac",
            path,
        };
        RunFfmpeg(args);
        return path;
    }

    /// <summary>
    /// A real, playable WAV whose own <c>data</c> chunk SIZE FIELD is zeroed out after ffmpeg writes it
    /// (T414 delta review finding 2) — ffmpeg itself reads a WAV's audio samples through to EOF
    /// regardless of what that field claims, so loudness stays fully measurable, but TagLib computes
    /// duration straight from that same field (byte count ÷ byte rate), so it reads back as exactly
    /// zero seconds — the OTHER of the two enrichment gates a corrupt/malformed asset can trip, never a
    /// throw. Verified by hand the same way as <see cref="CreateFlacNamedAsWav"/>: ffmpeg reports a
    /// real integrated LUFS + finite peak; a standalone TagLib read returns <c>Duration.TotalSeconds ==
    /// 0</c> without throwing.
    /// </summary>
    public static string CreateZeroDurationWav(string dir, string fileName, double seconds = 2.0, int frequency = 440)
    {
        var path = CreateTone(dir, fileName, seconds, frequency);
        var bytes = File.ReadAllBytes(path);
        var dataChunkIndex = IndexOfDataChunkId(bytes);
        if (dataChunkIndex < 0)
            throw new InvalidOperationException($"ffmpeg-generated WAV \"{fileName}\" has no \"data\" chunk to patch.");

        // The 4 bytes immediately after the "data" chunk id are its own little-endian UInt32 SIZE
        // field — zeroing them (rather than the sample bytes that follow) leaves every byte TagLib's
        // duration math and ffmpeg's own decode both still see identical, just mis-declared.
        Array.Clear(bytes, dataChunkIndex + 4, 4);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    static int IndexOfDataChunkId(byte[] bytes)
    {
        ReadOnlySpan<byte> marker = "data"u8;
        for (var i = 0; i + marker.Length <= bytes.Length; i++)
        {
            if (bytes.AsSpan(i, marker.Length).SequenceEqual(marker))
                return i;
        }

        return -1;
    }

    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gw-jingletest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void RunFfmpeg(IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start ffmpeg");
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"ffmpeg failed: {stderr.Result}");
    }
}
