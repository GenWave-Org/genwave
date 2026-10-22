using System.Diagnostics;
using System.Text.Json;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Synthesizes wavs with ffmpeg lavfi into a fresh temp folder, probes them with ffprobe, and deletes the folder on dispose.</summary>
internal sealed class ClipFixtureDirectory : IDisposable
{
    private readonly string directory = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "voiceoff-clips-" + Guid.NewGuid().ToString("N"))).FullName;

    internal string PathFor(string name) => Path.Combine(directory, name);

    /// <summary>A 6 s 440 Hz sine at about −30 LUFS integrated, 44.1 kHz stereo.</summary>
    internal async Task<string> QuietSineAsync(string name = "quiet.wav", params string[] extraOutputArgs)
    {
        string path = PathFor(name);
        await RunAsync(["-f", "lavfi", "-i", "sine=frequency=440:duration=6:sample_rate=44100,volume=-8.2dB,aformat=channel_layouts=stereo",
            .. extraOutputArgs, path]);
        return path;
    }

    internal async Task<string> SilenceAsync(string name, double seconds)
    {
        string path = PathFor(name);
        await RunAsync(["-f", "lavfi", "-i", $"anullsrc=r=24000:cl=mono:d={seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}", path]);
        return path;
    }

    /// <summary>A 6 s 440 Hz sine whose level steps up halfway, so it has a loudness range and a loudnorm linear apply is possible.</summary>
    internal async Task<string> SteppedSineAsync(string name = "stepped.wav")
    {
        string path = PathFor(name);
        await RunAsync(["-f", "lavfi", "-i", "aevalsrc=sin(2*PI*440*t)*if(lt(t\\,3)\\,0.02\\,0.1):d=6:s=44100", path]);
        return path;
    }

    /// <summary>A real wav with exactly one defect: its 12-byte RIFF header is zeroed, so it is not recognisable as audio.</summary>
    internal async Task<string> ZeroedHeaderAsync(string name)
    {
        string path = await SilenceAsync(name, 1.0);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        Array.Clear(bytes, 0, 12);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    internal static async Task<JsonDocument> ProbeAsync(string path)
    {
        string json = await CaptureAsync("ffprobe", ["-v", "error", "-show_format", "-show_streams", "-of", "json", path]);
        return JsonDocument.Parse(json);
    }

    private static async Task RunAsync(IEnumerable<string> args) =>
        await CaptureAsync("ffmpeg", ["-nostdin", "-hide_banner", "-y", .. args]);

    private static async Task<string> CaptureAsync(string tool, IEnumerable<string> args)
    {
        ProcessStartInfo psi = new(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException($"{tool} did not start");
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        string stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 ? stdout : throw new InvalidOperationException($"{tool} failed: {await stderr}");
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
