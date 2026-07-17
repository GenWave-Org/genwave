using System.Diagnostics;
using System.Globalization;
using GenWave.Core.Abstractions;

namespace GenWave.Loudness;

/// <summary>
/// Estimates tempo (BPM) for a media file by decoding its cue-trimmed body to a temporary PCM WAV via
/// ffmpeg, then running the Debian <c>aubio-tools</c> <c>aubio tempo</c> CLI over it (SPEC F46.1,
/// closes gitea-#190, with F47). ffmpeg does the decode because aubio's own codec support varies by build;
/// aubio reads files (not stdin), so — unlike <see cref="FfmpegEnergyAnalyzer"/>'s piped
/// "-f null -" measurement passes — the ffmpeg step writes a real temporary file that is deleted once
/// aubio has read it.
///
/// <c>aubio tempo</c> prints a single overall tempo estimate line of the form "<c>94.49 bpm</c>"
/// (verified against the real Debian <c>aubio-tools</c> binary 2026-07-14) — not per-beat timestamps;
/// aubio itself does the aggregation. <see cref="ParseTempo"/> takes the last non-empty line matching
/// that shape and rounds it to one decimal.
///
/// Returns <see langword="null"/> when:
/// <list type="bullet">
///   <item>ffmpeg exits non-zero (file unreadable / undecodable).</item>
///   <item>aubio exits non-zero.</item>
///   <item>aubio's output has no parseable "<c>&lt;float&gt; bpm</c>" line.</item>
/// </list>
/// The parsing and argument-construction logic below is factored into pure, <c>internal</c> functions
/// so it can be exercised directly against captured aubio output fixtures — the real binary is never
/// invoked in tests; CI runners don't carry it (SPEC F46.5).
/// </summary>
public sealed class AubioBpmAnalyzer : IBpmAnalyzer
{
    public async Task<double?> AnalyzeAsync(string path, double? cueInSec, double? cueOutSec, CancellationToken ct)
    {
        var wavPath = Path.Combine(Path.GetTempPath(), $"genwave-bpm-{Guid.NewGuid():N}.wav");
        try
        {
            var decoded = await DecodeToWavAsync(path, cueInSec, cueOutSec, wavPath, ct);
            if (!decoded)
                return null;

            var (exitCode, stdout) = await RunAubioTempoAsync(wavPath, ct);
            return InterpretAubioResult(exitCode, stdout);
        }
        finally
        {
            // No-op if DecodeToWavAsync never produced the file (e.g. ffmpeg failed before writing).
            File.Delete(wavPath);
        }
    }

    static async Task<bool> DecodeToWavAsync(
        string path,
        double? cueInSec,
        double? cueOutSec,
        string outputPath,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in BuildDecodeArguments(path, cueInSec, cueOutSec, outputPath))
            psi.ArgumentList.Add(arg);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);

        return p.ExitCode == 0;
    }

    static async Task<(int ExitCode, string Stdout)> RunAubioTempoAsync(string wavPath, CancellationToken ct)
    {
        using var p = Process.Start(new ProcessStartInfo("aubio")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            ArgumentList = { "tempo", wavPath }
        }) ?? throw new InvalidOperationException("Failed to start aubio.");

        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);

        return (p.ExitCode, stdout);
    }

    /// <summary>
    /// Builds the ffmpeg decode arguments: the cue-trimmed body of <paramref name="path"/> ([<paramref
    /// name="cueInSec"/> ?? 0, <paramref name="cueOutSec"/>] or to end-of-file when
    /// <paramref name="cueOutSec"/> is null), down-mixed to mono 44.1 kHz PCM WAV at
    /// <paramref name="outputPath"/>. Uses an <c>atrim</c> audio filter (absolute stream time, mirroring
    /// how <see cref="FfmpegEnergyAnalyzer"/> windows its own ffmpeg passes) rather than <c>-ss</c>/<c>-to</c>
    /// so a null cue bound needs no separate duration probe — an unbounded <c>atrim</c> trims to EOF.
    /// </summary>
    internal static IReadOnlyList<string> BuildDecodeArguments(
        string path,
        double? cueInSec,
        double? cueOutSec,
        string outputPath)
    {
        var start = (cueInSec ?? 0.0).ToString("G17", CultureInfo.InvariantCulture);
        var filter = cueOutSec is double end
            ? $"atrim=start={start}:end={end.ToString("G17", CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS"
            : $"atrim=start={start},asetpts=PTS-STARTPTS";

        return
        [
            "-nostdin", "-nostats", "-hide_banner",
            "-i", path,
            "-af", filter,
            "-ac", "1",
            "-ar", "44100",
            "-f", "wav",
            "-acodec", "pcm_s16le",
            "-y",
            outputPath
        ];
    }

    /// <summary>
    /// Interprets one <c>aubio tempo</c> run: a non-zero exit code is treated as indeterminate
    /// regardless of any output captured on stdout; otherwise delegates to <see cref="ParseTempo"/>.
    /// </summary>
    internal static double? InterpretAubioResult(int exitCode, string stdout) =>
        exitCode == 0 ? ParseTempo(stdout) : null;

    /// <summary>
    /// Parses <c>aubio tempo</c> stdout: the real Debian binary prints a single overall tempo
    /// estimate line ("<c>94.49 bpm</c>"), not per-beat timestamps — aubio itself does the
    /// aggregation. Takes the last non-empty line matching "<c>&lt;float&gt; bpm</c>" (invariant
    /// culture) and rounds it to one decimal. No matching line (empty output, usage/error text) is
    /// indeterminate — <see langword="null"/>.
    /// </summary>
    internal static double? ParseTempo(string aubioStdout)
    {
        const string suffix = " bpm";

        double? bpm = null;
        foreach (var rawLine in aubioStdout.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            // A non-bpm line (e.g. usage/error text, a warning) fails this match and is silently
            // skipped; the last matching line wins if more than one somehow appears.
            if (line.EndsWith(suffix, StringComparison.Ordinal) &&
                double.TryParse(
                    line[..^suffix.Length],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                bpm = value;
            }
        }

        return bpm is double parsed ? Math.Round(parsed, 1, MidpointRounding.AwayFromZero) : null;
    }
}
