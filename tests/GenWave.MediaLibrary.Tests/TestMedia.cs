using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace GenWave.MediaLibrary.Tests;

/// <summary>Generates small real audio files (and a deliberately corrupt one) for enrichment tests.</summary>
static class TestMedia
{
    /// <summary>A short sine tone with optional embedded tags, written via ffmpeg.</summary>
    public static string CreateTone(
        string dir, string fileName,
        string? title = null, string? artist = null, string? album = null, string? genre = null, int? year = null,
        double seconds = 2.0, int frequency = 440)
    {
        var path = Path.Combine(dir, fileName);
        var args = new List<string>
        {
            "-nostats", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"sine=frequency={frequency}:duration={seconds.ToString(CultureInfo.InvariantCulture)}",
            "-ar", "44100", "-ac", "2",
        };

        void Meta(string key, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            args.Add("-metadata");
            args.Add($"{key}={value}");
        }

        Meta("title", title);
        Meta("artist", artist);
        Meta("album", album);
        Meta("genre", genre);
        if (year is not null) Meta("date", year.Value.ToString(CultureInfo.InvariantCulture));

        args.Add(path);
        RunFfmpeg(args);
        return path;
    }

    /// <summary>A file with a media extension but non-audio bytes — enrichment must fail it, not crash.</summary>
    public static string CreateCorrupt(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("this is definitely not audio data"));
        return path;
    }

    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gw-libtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A WAV with <paramref name="silenceSec"/> of silence followed by a 1 kHz tone.</summary>
    public static string CreateSilenceThenTone(string dir, string fileName, double silenceSec = 3.0, double toneSec = 10.0)
    {
        var path = Path.Combine(dir, fileName);
        var silenceDur = silenceSec.ToString(CultureInfo.InvariantCulture);
        var toneDur = toneSec.ToString(CultureInfo.InvariantCulture);
        var filter =
            $"[0:a][1:a]concat=n=2:v=0:a=1[out]";
        var args = new List<string>
        {
            "-nostats", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"aevalsrc=0:d={silenceDur}",
            "-f", "lavfi", "-i", $"sine=frequency=1000:duration={toneDur}",
            "-filter_complex", filter,
            "-map", "[out]",
            "-ar", "44100", "-ac", "2",
            path
        };
        RunFfmpeg(args);
        return path;
    }

    /// <summary>A WAV with a 1 kHz tone followed by <paramref name="silenceSec"/> of silence.</summary>
    public static string CreateToneThenSilence(string dir, string fileName, double toneSec = 10.0, double silenceSec = 4.0)
    {
        var path = Path.Combine(dir, fileName);
        var toneDur = toneSec.ToString(CultureInfo.InvariantCulture);
        var silenceDur = silenceSec.ToString(CultureInfo.InvariantCulture);
        var filter =
            $"[0:a][1:a]concat=n=2:v=0:a=1[out]";
        var args = new List<string>
        {
            "-nostats", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"sine=frequency=1000:duration={toneDur}",
            "-f", "lavfi", "-i", $"aevalsrc=0:d={silenceDur}",
            "-filter_complex", filter,
            "-map", "[out]",
            "-ar", "44100", "-ac", "2",
            path
        };
        RunFfmpeg(args);
        return path;
    }

    /// <summary>A WAV containing only a 1 kHz tone — no silence at start or end.</summary>
    public static string CreateToneOnly(string dir, string fileName, double durationSec = 10.0)
    {
        return CreateTone(dir, fileName, seconds: durationSec, frequency: 1000);
    }

    /// <summary>
    /// A WAV whose first 15 s is a full-volume 1 kHz sine (loud intro), followed by 15 s of silence.
    /// Suitable for energy analysis: intro window measures loud content, outro measures silence.
    /// </summary>
    public static string CreateLoudIntroFile(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        var args = new List<string>
        {
            "-nostats", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "sine=frequency=1000:duration=15",
            "-f", "lavfi", "-i", "aevalsrc=0:d=15",
            "-filter_complex", "[0:a][1:a]concat=n=2:v=0:a=1[out]",
            "-map", "[out]",
            "-ar", "44100", "-ac", "2",
            path
        };
        RunFfmpeg(args);
        return path;
    }

    /// <summary>
    /// A WAV whose first 15 s is a low-volume (-20 dB) 1 kHz sine (quiet intro), followed by 15 s of silence.
    /// Intro energy is measurably lower than <see cref="CreateLoudIntroFile"/>.
    /// </summary>
    public static string CreateQuietIntroFile(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        var args = new List<string>
        {
            "-nostats", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "sine=frequency=1000:duration=15,volume=-20dB",
            "-f", "lavfi", "-i", "aevalsrc=0:d=15",
            "-filter_complex", "[0:a][1:a]concat=n=2:v=0:a=1[out]",
            "-map", "[out]",
            "-ar", "44100", "-ac", "2",
            path
        };
        RunFfmpeg(args);
        return path;
    }

    /// <summary>
    /// A WAV with <paramref name="leadingSilenceSec"/> of silence, then a tone at
    /// <paramref name="frequency"/> Hz for <paramref name="toneSec"/>, then
    /// <paramref name="trailingSilenceSec"/> of silence. Used to prove a bed's cue points bound the
    /// audio a mix actually uses — the tone region is the only part that should ever reach the loop.
    /// </summary>
    public static string CreateSilenceToneSilence(
        string dir, string fileName,
        double leadingSilenceSec, double toneSec, double trailingSilenceSec, int frequency = 1000)
    {
        var path = Path.Combine(dir, fileName);
        var lead = leadingSilenceSec.ToString(CultureInfo.InvariantCulture);
        var tone = toneSec.ToString(CultureInfo.InvariantCulture);
        var trail = trailingSilenceSec.ToString(CultureInfo.InvariantCulture);
        var args = new List<string>
        {
            "-nostats", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"aevalsrc=0:d={lead}",
            "-f", "lavfi", "-i", $"sine=frequency={frequency}:duration={tone}",
            "-f", "lavfi", "-i", $"aevalsrc=0:d={trail}",
            "-filter_complex", "[0:a][1:a][2:a]concat=n=3:v=0:a=1[out]",
            "-map", "[out]",
            "-ar", "44100", "-ac", "2",
            path
        };
        RunFfmpeg(args);
        return path;
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
