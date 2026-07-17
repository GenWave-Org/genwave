using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Loudness;

/// <summary>
/// Measures intro and outro energy for a media file by running ffmpeg's ebur128 filter over
/// cue-trimmed head and tail windows (STORY-031, Epic H).
///
/// The intro window covers [cueInSec, cueInSec + WindowSeconds]; the outro window covers
/// [cueOutSec - WindowSeconds, cueOutSec]. When cue times are null the raw file head/tail is
/// used instead (intro = [0, WindowSeconds]; outro = [duration - WindowSeconds, duration]).
/// Each window is measured independently via a separate ffmpeg invocation so the
/// atrim filter can precisely scope the loudness measurement.
///
/// Returns <see langword="null"/> when:
/// <list type="bullet">
///   <item>ffmpeg exits non-zero (file unreadable / missing / undecodable).</item>
///   <item>The ebur128 summary block cannot be parsed from stderr.</item>
/// </list>
/// Silence (gated at −70 LUFS by ebur128) normalizes to 0.0, not null.
/// </summary>
public sealed partial class FfmpegEnergyAnalyzer : IEnergyAnalyzer
{
    // TUNABLE: Normalization range for absolute short-term (integrated-over-window) LUFS → [0,1].
    //
    // Choice: absolute LUFS range [-36, -6] → [0.0, 1.0], clamped, monotonic.
    //
    // Rationale: radio content after loudness normalization sits roughly in [-30, -9] LUFS.
    // A floor of -36 LUFS covers very quiet passages; a ceiling of -6 LUFS covers
    // hard-limited loud content. This keeps the mapping stable regardless of the track's
    // integrated loudness (i.e. it does NOT require the per-track integrated loudness as a
    // reference). The E10 listening test should validate whether these bounds match the
    // perceptual intent; adjust MinLufs / MaxLufs to re-scale.
    const double MinLufs = -36.0;
    const double MaxLufs = -6.0;

    // Silence gate: ebur128 reports -70.0 LUFS (or -inf) for windows it cannot measure.
    // A gated value is clamped to 0.0 (silence) rather than treated as a parse failure.
    const double GateFloor = -70.0;

    readonly IOptionsMonitor<EnergyOptions> options;

    public FfmpegEnergyAnalyzer(IOptionsMonitor<EnergyOptions> options)
    {
        this.options = options;
    }

    public async Task<EnergyPoints?> AnalyzeAsync(
        string path,
        double? cueInSec,
        double? cueOutSec,
        CancellationToken ct)
    {
        // Read fresh per call (SPEC F44.3, closes gitea-#197) — never a boot-frozen field — so a live
        // edit to Library:Energy:WindowSeconds applies the NEXT time any file is (re-)analyzed; an
        // already-enriched row is unaffected until it is re-enriched.
        var window = options.CurrentValue.WindowSeconds;

        // Intro window: [cueInSec, cueInSec + window]; null cueIn → [0, window].
        var introStart = cueInSec ?? 0.0;
        var introEnd = introStart + window;

        // Outro window: [cueOutSec - window, cueOutSec]; null cueOut → [duration - window, duration].
        // We must resolve the file duration when cueOutSec is absent so that atrim gets a real
        // positive start offset — atrim does not support negative start values.
        double outroStart;
        double outroEnd;
        if (cueOutSec.HasValue)
        {
            outroEnd = cueOutSec.Value;
            outroStart = Math.Max(0.0, outroEnd - window);
        }
        else
        {
            var duration = await GetFileDurationAsync(path, ct);
            if (duration is null)
                return null;

            outroEnd = duration.Value;
            outroStart = Math.Max(0.0, outroEnd - window);
        }

        var introLufs = await MeasureWindowAsync(path, introStart, introEnd, ct);
        if (introLufs is null)
            return null;

        var outroLufs = await MeasureWindowAsync(path, outroStart, outroEnd, ct);
        if (outroLufs is null)
            return null;

        return new EnergyPoints(
            IntroEnergy: Normalize(introLufs.Value),
            OutroEnergy: Normalize(outroLufs.Value));
    }

    /// <summary>
    /// Probes <paramref name="path"/> with ffmpeg to obtain the container duration in seconds.
    /// Returns null when ffmpeg exits non-zero or the Duration header cannot be parsed.
    /// </summary>
    async Task<double?> GetFileDurationAsync(string path, CancellationToken ct)
    {
        using var p = Process.Start(new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList = { "-nostdin", "-i", path, "-f", "null", "-" }
        }) ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        var stderr = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);

        // ffmpeg exits non-zero when given only -i with no output filter, but still emits
        // the full input header including Duration — parse it regardless of exit code.
        return ParseDuration(stderr);
    }

    /// <summary>
    /// Runs ffmpeg over a single window [startSec, endSec] of <paramref name="path"/>,
    /// measuring integrated loudness (LUFS) via ebur128.
    /// Returns null only on ffmpeg failure (non-zero exit) or when the ebur128 Summary
    /// block is entirely absent from stderr. Silence (gated output) returns the raw LUFS
    /// value so the caller can clamp it to 0.0 via <see cref="Normalize"/>.
    /// </summary>
    async Task<double?> MeasureWindowAsync(
        string path,
        double startSec,
        double endSec,
        CancellationToken ct)
    {
        var start = startSec.ToString("G17", CultureInfo.InvariantCulture);
        var end = endSec.ToString("G17", CultureInfo.InvariantCulture);
        var atrimFilter = $"atrim=start={start}:end={end},asetpts=PTS-STARTPTS,ebur128";

        using var p = Process.Start(new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList =
            {
                "-nostdin", "-nostats", "-hide_banner",
                "-i", path,
                "-filter_complex", atrimFilter,
                "-f", "null", "-"
            }
        }) ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        var stderr = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);

        if (p.ExitCode != 0)
            return null;

        return ParseIntegratedLufs(stderr);
    }

    /// <summary>
    /// Parses the integrated loudness "I:" value from the ebur128 Summary block.
    /// Returns null when the Summary block is absent or the value is not parseable.
    /// A gated (silence) reading (≤ GateFloor or -inf) is returned as-is so that
    /// <see cref="Normalize"/> can clamp it to 0.0 — null is reserved for genuine
    /// measurement failure.
    /// </summary>
    static double? ParseIntegratedLufs(string stderr)
    {
        var summaryIdx = stderr.LastIndexOf("Summary:", StringComparison.Ordinal);
        if (summaryIdx < 0)
            return null;

        var summary = stderr[summaryIdx..];
        var m = IntegratedLufsRx().Match(summary);
        if (!m.Success)
            return null;

        if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lufs))
            return null;

        // Return gated/silence values (≤ GateFloor or non-finite) as a very-negative-LUFS
        // reading. Normalize() will clamp them to 0.0 (silence). Do NOT return null here —
        // null means "ffmpeg failed", not "window is silent".
        if (!double.IsFinite(lufs) || lufs <= GateFloor)
            return GateFloor;

        return lufs;
    }

    /// <summary>
    /// Maps an absolute LUFS value to [0, 1] over the range [MinLufs, MaxLufs].
    /// Values at or below MinLufs clamp to 0.0; values at or above MaxLufs clamp to 1.0.
    /// GateFloor (−70 LUFS) is well below MinLufs (−36 LUFS) so silence → 0.0.
    /// </summary>
    static double Normalize(double lufs)
    {
        // TUNABLE: adjust MinLufs/MaxLufs constants above after E10 listening test.
        var clamped = Math.Clamp(lufs, MinLufs, MaxLufs);
        return (clamped - MinLufs) / (MaxLufs - MinLufs);
    }

    /// <summary>
    /// Parses the file duration from the "Duration: HH:MM:SS.ss" header in ffmpeg stderr.
    /// Returns null when the line is absent (stdin source, no container header, etc.).
    /// </summary>
    static double? ParseDuration(string stderr)
    {
        var m = DurationRx().Match(stderr);
        if (!m.Success) return null;

        if (!int.TryParse(m.Groups[1].Value, out var hours)) return null;
        if (!int.TryParse(m.Groups[2].Value, out var minutes)) return null;
        if (!double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return null;

        return hours * 3600.0 + minutes * 60.0 + seconds;
    }

    // Matches the "I:  -21.1 LUFS" line in the ebur128 Summary block.
    [GeneratedRegex(@"I:\s*(-?[\d.]+)\s*LUFS")]
    private static partial Regex IntegratedLufsRx();

    // Matches: Duration: 00:00:30.00
    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):([\d.]+)")]
    private static partial Regex DurationRx();
}
