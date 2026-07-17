using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Loudness;

/// <summary>
/// Detects silence-trimmed cue points (cue_in / cue_out) by invoking ffmpeg's silencedetect filter and
/// parsing the silence_start / silence_end events from stderr (SPEC F13.7).
///
/// Returns <see langword="null"/> when:
/// <list type="bullet">
///   <item>ffmpeg exits non-zero (file unreadable / missing).</item>
///   <item>No silence is detected — full-file playback is intended; blank.eat acts as the runtime backstop.</item>
/// </list>
/// </summary>
public sealed partial class FfmpegCueAnalyzer : ICueAnalyzer
{
    readonly IOptionsMonitor<CueDetectionOptions> options;

    public FfmpegCueAnalyzer(IOptionsMonitor<CueDetectionOptions> options)
    {
        this.options = options;
    }

    public async Task<CuePoints?> AnalyzeAsync(string path, CancellationToken ct)
    {
        // Read fresh per call (SPEC F44.3, closes gitea-#197) — never a boot-frozen field — so a live
        // edit to Library:CueDetection:MinSilenceDurationSec applies the NEXT time any file is
        // (re-)analyzed; an already-enriched row is unaffected until it is re-enriched. SilenceThresholdDb
        // is NOT operator-editable (F44.4 — locked to the engine's hardcoded blank.eat threshold).
        var cfg = options.CurrentValue;
        var threshold = cfg.SilenceThresholdDb.ToString("F1", CultureInfo.InvariantCulture);
        var minDuration = cfg.MinSilenceDurationSec.ToString("G", CultureInfo.InvariantCulture);
        var filterArg = $"silencedetect=noise={threshold}dB:duration={minDuration}";

        using var p = Process.Start(new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList = { "-nostdin", "-i", path, "-af", filterArg, "-f", "null", "-" }
        }) ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        var stderr = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);

        if (p.ExitCode != 0)
            return null;

        var duration = ParseDuration(stderr);
        return ParseCuePoints(stderr, duration);
    }

    /// <summary>
    /// Parses silence_start / silence_end events from ffmpeg silencedetect stderr output into cue points.
    ///
    /// Events arrive in order: silence_start / silence_end pairs for each silence region.
    ///
    /// Rules:
    ///   - No silence events at all → null (blank.eat is the runtime backstop).
    ///   - Leading silence  = first region whose start is at or within <see cref="LeadingEpsilonSec"/> of 0.
    ///                        CueIn = that region's end time.
    ///   - Trailing silence = last region that begins AFTER the leading region (or after 0 if none).
    ///                        CueOut = that region's start time.
    ///                        If no trailing silence: CueOut = <paramref name="fileDurationSec"/> (whole track audible to EOF).
    ///   - Entirely silent (one region spanning ~full duration, no audible content) → null.
    /// </summary>
    static CuePoints? ParseCuePoints(string stderr, double? fileDurationSec)
    {
        var startMatches = SilenceStartRx().Matches(stderr);
        var endMatches = SilenceEndRx().Matches(stderr);

        if (startMatches.Count == 0)
            return null;

        var starts = new List<double>(startMatches.Count);
        var ends = new List<double>(endMatches.Count);

        foreach (Match m in startMatches)
        {
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                starts.Add(t);
        }

        foreach (Match m in endMatches)
        {
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                ends.Add(t);
        }

        if (starts.Count == 0)
            return null;

        double cueIn = 0.0;
        double? cueOut = null;

        // Leading silence: first region starts at or very near the file head.
        bool hasLeading = starts[0] <= LeadingEpsilonSec && ends.Count > 0;
        if (hasLeading)
            cueIn = ends[0];

        // Trailing silence: last region whose start is after the leading region ends (or after epsilon).
        var lastStart = starts[^1];
        if (lastStart > LeadingEpsilonSec)
            cueOut = lastStart;

        // If we have only a leading region and it covers most of the file, the content is entirely silent.
        if (hasLeading && cueOut is null)
        {
            // We detected leading silence but no trailing region. Use the file duration as CueOut so
            // the full audible content (from cueIn to EOF) can be expressed.
            if (fileDurationSec is null)
                return null;

            var resolvedCueOut = fileDurationSec.Value;
            if (cueIn >= resolvedCueOut)
                return null;   // File is entirely/mostly silent — no usable content.

            return new CuePoints(cueIn, resolvedCueOut);
        }

        if (!hasLeading && cueOut is null)
            return null;

        var finalCueOut = cueOut ?? fileDurationSec;
        if (finalCueOut is null)
            return null;

        if (cueIn >= finalCueOut.Value)
            return null;

        return new CuePoints(cueIn, finalCueOut.Value);
    }

    /// <summary>
    /// Parses the file duration from ffmpeg's "Duration: HH:MM:SS.ss" header line.
    /// Returns null if the duration cannot be parsed (e.g. the input has no container duration).
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

    // Small epsilon to classify a silence_start as "at file head" (accounts for sample-boundary rounding).
    const double LeadingEpsilonSec = 0.1;

    // Matches: silence_start: 1.23456
    [GeneratedRegex(@"silence_start:\s*(-?[\d.]+)")]
    private static partial Regex SilenceStartRx();

    // Matches: silence_end: 3.456 (the pipe-delimited duration field is ignored)
    [GeneratedRegex(@"silence_end:\s*(-?[\d.]+)")]
    private static partial Regex SilenceEndRx();

    // Matches: Duration: 00:00:13.00
    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):([\d.]+)")]
    private static partial Regex DurationRx();
}
