using System.Diagnostics;
using System.Globalization;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>
/// Real sine-tone wavs for the reel facts, one per gauntlet line number, each a distinct length (<see cref="SecondsFor"/>) so a reel's order shows in where its silences fall.
/// Made with ffmpeg on first use, kept in a temp folder, and deleted on dispose.
/// </summary>
internal sealed class ToneClips : IDisposable
{
    private readonly Lazy<string> directory = new(() => Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "voiceoff-tones-" + Guid.NewGuid().ToString("N"))).FullName);

    /// <summary>Line 1 is 0.5 s, line 2 is 0.7 s, and so on up by 0.2 s.</summary>
    internal static double SecondsFor(int lineNumber) => 0.3 + (0.2 * lineNumber);

    internal void CopyTo(int lineNumber, string destination)
    {
        string source = Path.Combine(directory.Value, lineNumber.ToString(CultureInfo.InvariantCulture) + ".wav");
        if (!File.Exists(source))
        {
            Make(lineNumber, source);
        }

        File.Copy(source, destination, overwrite: true);
    }

    private static void Make(int lineNumber, string path)
    {
        string input = string.Create(CultureInfo.InvariantCulture, $"sine=frequency={200 + (60 * lineNumber)}:duration={SecondsFor(lineNumber)}:sample_rate=24000");
        ProcessStartInfo psi = new("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
        foreach (string arg in new[] { "-nostdin", "-hide_banner", "-v", "error", "-y", "-f", "lavfi", "-i", input, "-ac", "1", "-c:a", "pcm_s16le", path })
        {
            psi.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start.");
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("ffmpeg could not make a tone: " + error);
        }
    }

    public void Dispose()
    {
        if (directory.IsValueCreated)
        {
            Directory.Delete(directory.Value, recursive: true);
        }
    }
}
