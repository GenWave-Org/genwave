using System.Globalization;
using GenWave.Core.Abstractions;
using GenWave.Loudness;

namespace GenWave.VoiceOff.Audio;

/// <summary>
/// Matches a render to the tool's loudness target before it is paired, reeled, or listened to (SPEC F214.4):
/// a two-pass ffmpeg <c>loudnorm</c> (measure, then linear apply), a verify of the result, and the
/// <see cref="ClipScrubber"/> format. A peaky clip (peaks more than 14.5 dB over its loudness) cannot take a linear
/// gain under the true-peak ceiling, and loudnorm's dynamic fallback lands short clips 1–4 LU under the target; only
/// such a clip goes through a gain to the target into a peak limiter first, so every other clip stays untouched. A clip
/// that still verifies off target gets one gain trim by the verify's own reading: loudnorm's meter and the verify's can
/// disagree by ~1 LU on the same clip. The output path only ever holds a verified clip — on any failure
/// nothing is left there, so nothing unmeasurable can be paired.
/// </summary>
internal sealed class LoudnessMatch
{
    // Mirrors the station's Loudness:TargetLufs default (LoudnessOptions); the tool references no Host code.
    internal const double TargetLufs = -16.0;
    internal const double TruePeakDbtp = -1.5;
    internal const double LoudnessRange = 11.0;
    internal const double ToleranceLu = 1.0;

    // 1.5 dB under the true-peak ceiling: room for loudnorm's linear step to make up what the limiter took.
    internal const double LimiterCeilingDbfs = -3.0;

    private readonly ILoudnessAnalyzer verifier;

    internal LoudnessMatch(ILoudnessAnalyzer verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        this.verifier = verifier;
    }

    internal LoudnessMatch() : this(new FfmpegLoudnessAnalyzer())
    {
    }

    /// <summary>The first-pass filter: measure only, JSON report on stderr.</summary>
    internal static string MeasureFilter() =>
        string.Create(CultureInfo.InvariantCulture, $"{ClipScrubber.FormatPreFilter}{Loudnorm}:print_format=json");

    /// <summary>The measure filter behind <see cref="LimitFilter"/>: what loudnorm sees once the gain and limiter have run.</summary>
    internal static string MeasureFilter(double gainDb) =>
        string.Create(CultureInfo.InvariantCulture, $"{LimitFilter(gainDb)}{Loudnorm}:print_format=json");

    /// <summary>The apply filter: linear apply of <paramref name="m"/>, behind the same <see cref="LimitFilter"/> it was measured
    /// through when <paramref name="gainDb"/> is set; the JSON report names the mode loudnorm actually used.</summary>
    internal static string ApplyFilter(LoudnormMeasurement m, double? gainDb = null) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{(gainDb is { } gain ? LimitFilter(gain) : ClipScrubber.FormatPreFilter)}{Loudnorm}:measured_I={m.InputI}:measured_TP={m.InputTp}:measured_LRA={m.InputLra}:measured_thresh={m.InputThresh}:offset={m.TargetOffset}:linear=true:print_format=json");

    /// <summary>The format, a gain of <paramref name="gainDb"/>, and a delay-compensated limiter at <see cref="LimiterCeilingDbfs"/> with its auto level off.</summary>
    internal static string LimitFilter(double gainDb) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{ClipScrubber.FormatPreFilter}volume={gainDb:0.00}dB,alimiter=limit={Math.Pow(10, LimiterCeilingDbfs / 20):0.0000}:level=false:latency=true,");

    /// <summary>The verify-miss trim: the format, a gain of <paramref name="gainDb"/>, and a limiter at the true-peak ceiling in case the gain is up.</summary>
    internal static string TrimFilter(double gainDb) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{ClipScrubber.FormatPreFilter}volume={gainDb:0.00}dB,alimiter=limit={Math.Pow(10, TruePeakDbtp / 20):0.0000}:level=false:latency=true");

    private static string Loudnorm =>
        string.Create(CultureInfo.InvariantCulture, $"loudnorm=I={TargetLufs}:TP={TruePeakDbtp}:LRA={LoudnessRange}");

    /// <summary>Matches <paramref name="input"/> into <paramref name="output"/>; never throws for an engine-quality problem.</summary>
    internal async Task<LoudnessMatchResult> MatchAsync(string input, string output, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);

        string temp = Path.GetFullPath(output) + ".matching";
        try
        {
            LoudnessMatchResult result = await MatchToTempAsync(input, output, temp, ct);
            if (!result.Ok)
            {
                // A failed render is never paired: not even a stale clip from an earlier run may stay at the output.
                DeleteIfPresent(output);
            }

            return result;
        }
        finally
        {
            DeleteIfPresent(temp);
            DeleteIfPresent(TrimPath(temp));
        }
    }

    // File.Delete throws when the parent folder is missing; a missing file is the success case here.
    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private async Task<LoudnessMatchResult> MatchToTempAsync(string input, string output, string temp, CancellationToken ct)
    {
        FfmpegRunResult measure = await FfmpegProcess.RunFfmpegCapturingStderrAsync(
            ClipScrubber.MeasureArgs(input, MeasureFilter()), ct);
        if (!measure.Succeeded)
        {
            return LoudnessMatchResult.Failure($"ffmpeg could not read the clip (exit {measure.ExitCode})");
        }

        LoudnormMeasurement? measurement = LoudnormMeasurement.TryParse(measure.Stderr);
        if (measurement is null)
        {
            return LoudnessMatchResult.Failure("loudness could not be measured");
        }

        double? gainDb = null;
        if (measurement.TruePeakDbtp - measurement.IntegratedLufs > TruePeakDbtp - TargetLufs)
        {
            (LoudnormMeasurement? limited, double limitGainDb) = await MeasureBehindLimiterAsync(input, measurement, ct);
            if (limited is null)
            {
                return LoudnessMatchResult.Failure("loudness could not be measured behind the limiter");
            }

            (measurement, gainDb) = (limited, limitGainDb);
        }

        FfmpegRunResult apply = await ClipScrubber.ScrubAsync(input, temp, ApplyFilter(measurement, gainDb), ct);
        if (!apply.Succeeded)
        {
            return LoudnessMatchResult.Failure($"ffmpeg could not write the matched clip (exit {apply.ExitCode})");
        }

        var verified = await verifier.AnalyzeAsync(temp, ct);
        if (verified.Measurable && IsOffTarget(verified.IntegratedLufs))
        {
            FfmpegRunResult trim = await ClipScrubber.ScrubAsync(temp, TrimPath(temp), TrimFilter(TargetLufs - verified.IntegratedLufs), ct);
            if (!trim.Succeeded)
            {
                return LoudnessMatchResult.Failure($"ffmpeg could not trim the matched clip (exit {trim.ExitCode})");
            }

            File.Move(TrimPath(temp), temp, overwrite: true);
            verified = await verifier.AnalyzeAsync(temp, ct);
        }

        if (!verified.Measurable)
        {
            return LoudnessMatchResult.Failure("matched clip could not be measured");
        }

        if (IsOffTarget(verified.IntegratedLufs))
        {
            return LoudnessMatchResult.Failure(
                string.Create(CultureInfo.InvariantCulture, $"matched clip measures {verified.IntegratedLufs:0.0} LUFS, not {TargetLufs:0} ±{ToleranceLu:0}"),
                verified.IntegratedLufs);
        }

        File.Move(temp, output, overwrite: true);
        return LoudnessMatchResult.Success(verified.IntegratedLufs, LoudnormMeasurement.IsLinear(apply.Stderr));
    }

    private static bool IsOffTarget(double lufs) => Math.Abs(lufs - TargetLufs) > ToleranceLu;

    private static string TrimPath(string temp) => temp + ".trim";

    // Gain to the target, then once more by what the limiter took off (a peaky clip loses up to ~3 LU to it); null when a pass cannot measure.
    private static async Task<(LoudnormMeasurement? Measurement, double GainDb)> MeasureBehindLimiterAsync(
        string input, LoudnormMeasurement raw, CancellationToken ct)
    {
        double gainDb = TargetLufs - raw.IntegratedLufs;
        LoudnormMeasurement? measurement = null;
        for (int pass = 0; pass < 2; pass++)
        {
            FfmpegRunResult measure = await FfmpegProcess.RunFfmpegCapturingStderrAsync(ClipScrubber.MeasureArgs(input, MeasureFilter(gainDb)), ct);
            measurement = measure.Succeeded ? LoudnormMeasurement.TryParse(measure.Stderr) : null;
            if (measurement is null)
            {
                return (null, gainDb);
            }

            if (pass == 0)
            {
                gainDb += TargetLufs - measurement.IntegratedLufs;
            }
        }

        return (measurement, gainDb);
    }
}
