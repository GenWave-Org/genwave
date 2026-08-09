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
        AnalyzerProcessPriority.TryLower(p);

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
    /// Events arrive in order: silence_start / silence_end pairs for each silence region; the final
    /// region may be open-ended (a silence_start with no silence_end) when the file ends silent.
    ///
    /// Rules:
    ///   - No silence events at all → null (blank.eat is the runtime backstop).
    ///   - Leading silence  = first region whose start is at or within <see cref="LeadingEpsilonSec"/> of 0.
    ///                        CueIn = that region's end time.
    ///   - Trailing silence = a final region that actually extends to EOF: open-ended, or closed within
    ///                        <see cref="TrailingEpsilonSec"/> of the container duration.
    ///                        CueOut = that region's start time.
    ///   - Interior silence (a region that ends before EOF) never sets CueOut (gh-#424) — a TTS
    ///                        sentence pause or a quiet mid-track break is content, not a tail to trim.
    ///   - If no trailing silence: CueOut = <paramref name="fileDurationSec"/> (whole track audible to EOF).
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

        // A single region opening at the head and never ending is a fully-silent file.
        if (starts[0] <= LeadingEpsilonSec && ends.Count == 0)
            return null;

        double cueIn = 0.0;
        double? cueOut = null;

        // Leading silence: first region starts at or very near the file head.
        bool hasLeading = starts[0] <= LeadingEpsilonSec && ends.Count > 0;
        if (hasLeading)
            cueIn = ends[0];

        // Trailing silence must actually extend to EOF (gh-#424): events alternate start/end, so the
        // last region is open-ended (still silent at EOF) when there is one more start than there are
        // ends; a closed region also counts as trailing when its end lands within TrailingEpsilonSec
        // of the container duration. A region that ends earlier than that is an INTERIOR pause — a
        // TTS sentence gap (0.6 s injected pauses clear the 0.5 s detection floor), a quiet mid-track
        // break — and must never become cue_out: that cut the final sentence of every multi-sentence
        // patter clip on air.
        var lastStart = starts[^1];
        double? lastEnd = ends.Count == starts.Count ? ends[^1] : null;
        var lastRegionRunsToEof = lastEnd is null
            || (fileDurationSec is not null && lastEnd.Value >= fileDurationSec.Value - TrailingEpsilonSec);
        if (lastStart > LeadingEpsilonSec && lastRegionRunsToEof)
            cueOut = lastStart;

        // No trailing region to trim: the audible content runs to EOF, so express the full extent —
        // duration-dependent consumers (F66.1 DurationMs, straddle boundary fit) still need the
        // measurement. Without a container duration there is nothing usable to report; runtime
        // blank.eat remains the backstop either way.
        var finalCueOut = cueOut ?? fileDurationSec;
        if (finalCueOut is null)
            return null;

        if (cueIn >= finalCueOut.Value)
            return null;   // File is entirely/mostly silent — no usable content.

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

    // A closed silence region whose end lands within this of the container duration still counts as
    // trailing: ffmpeg flushes a final silence_end at EOF on some builds, and container duration
    // (especially bitrate-estimated) can drift slightly from decoded timestamps. Misclassifying in
    // the safe direction (trailing treated as interior → no trim → blank.eat backstop) is preferred
    // over the unsafe one (interior treated as trailing → truncated content).
    const double TrailingEpsilonSec = 0.25;

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
