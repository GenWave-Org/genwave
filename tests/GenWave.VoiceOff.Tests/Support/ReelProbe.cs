using System.Globalization;
using System.Text.RegularExpressions;
using GenWave.Loudness;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Measures a built reel with the same tools the station uses: ffprobe for its length, ffmpeg silencedetect for where its gaps start, and the loudness analyzer.</summary>
internal static partial class ReelProbe
{
    [GeneratedRegex(@"silence_start: (?<t>[0-9.]+)")]
    private static partial Regex SilenceStart();

    internal static double Seconds(string path) =>
        FfmpegProcess.ProbeDurationSecondsAsync(path, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Where each stretch of digital silence of at least 0.3 s starts, in seconds.</summary>
    internal static IReadOnlyList<double> GapStarts(string path)
    {
        FfmpegRunResult run = FfmpegProcess.RunFfmpegCapturingStderrAsync(
            ["-nostdin", "-hide_banner", "-i", path, "-af", "silencedetect=n=-60dB:d=0.3", "-f", "null", "-"], CancellationToken.None).GetAwaiter().GetResult();
        return [.. SilenceStart().Matches(run.Stderr).Select(m => double.Parse(m.Groups["t"].Value, CultureInfo.InvariantCulture))];
    }

    internal static double IntegratedLufs(string path) =>
        new FfmpegLoudnessAnalyzer().AnalyzeAsync(path, CancellationToken.None).GetAwaiter().GetResult().IntegratedLufs;
}
