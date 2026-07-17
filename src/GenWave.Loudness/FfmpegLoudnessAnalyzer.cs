using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using GenWave.Core.Abstractions;
using LoudnessMeasurement = GenWave.Core.Domain.Loudness;

namespace GenWave.Loudness;

/// <summary>
/// Measures integrated loudness (LUFS) and true peak (dBTP) via FFmpeg's ebur128 filter (PRD §8).
/// Relocated from playout into the library: the library MEASURES and stores; playout APPLIES the gain
/// at push time. Parses the trailing Summary block on stderr — not the 10 Hz per-frame lines.
/// Silence/near-silence (gated at −70 LUFS, or a non-finite peak) is flagged unmeasurable so it is
/// never auto-amplified.
/// </summary>
public sealed partial class FfmpegLoudnessAnalyzer : ILoudnessAnalyzer
{
    const double GateFloor = -70.0;

    public async Task<LoudnessMeasurement> AnalyzeAsync(string path, CancellationToken ct)
    {
        using var p = Process.Start(new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList = { "-nostats", "-hide_banner", "-i", path,
                             "-filter_complex", "ebur128=peak=true", "-f", "null", "-" }
        }) ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        var err = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);

        var summary = err[Math.Max(0, err.LastIndexOf("Summary:", StringComparison.Ordinal))..];
        var lufs = Parse(IntegratedRx().Match(summary));
        var tp = Parse(TruePeakRx().Match(summary));
        return new LoudnessMeasurement(lufs, tp, lufs > GateFloor && double.IsFinite(tp));
    }

    static double Parse(Match m) =>
        m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : double.NegativeInfinity;

    [GeneratedRegex(@"I:\s*(-?[\d.]+)\s*LUFS")]
    private static partial Regex IntegratedRx();

    [GeneratedRegex(@"Peak:\s*(-?[\d.]+)\s*dBFS")]
    private static partial Regex TruePeakRx();
}
