using System.Globalization;
using GenWave.Core.Abstractions;
using GenWave.Loudness;

namespace GenWave.VoiceOff.Audio;

/// <summary>
/// Matches a render to the tool's loudness target before it is paired, reeled, or listened to (SPEC F214.4):
/// a two-pass ffmpeg <c>loudnorm</c> (measure, then linear apply), a verify of the result, and the
/// <see cref="ClipScrubber"/> format. The output path only ever holds a verified clip — on any failure
/// nothing is left there, so nothing unmeasurable can be paired.
/// </summary>
internal sealed class LoudnessMatch
{
    // Mirrors the station's Loudness:TargetLufs default (LoudnessOptions); the tool references no Host code.
    internal const double TargetLufs = -16.0;
    internal const double TruePeakDbtp = -1.5;
    internal const double LoudnessRange = 11.0;
    internal const double ToleranceLu = 1.0;

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
        string.Create(CultureInfo.InvariantCulture, $"{ClipScrubber.FormatPreFilter}loudnorm=I={TargetLufs}:TP={TruePeakDbtp}:LRA={LoudnessRange}:print_format=json");

    /// <summary>The second-pass filter: linear apply of the first-pass measurement; the JSON report names the mode loudnorm actually used.</summary>
    internal static string ApplyFilter(LoudnormMeasurement m) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{ClipScrubber.FormatPreFilter}loudnorm=I={TargetLufs}:TP={TruePeakDbtp}:LRA={LoudnessRange}:measured_I={m.InputI}:measured_TP={m.InputTp}:measured_LRA={m.InputLra}:measured_thresh={m.InputThresh}:offset={m.TargetOffset}:linear=true:print_format=json");

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

        FfmpegRunResult apply = await ClipScrubber.ScrubAsync(input, temp, ApplyFilter(measurement), ct);
        if (!apply.Succeeded)
        {
            return LoudnessMatchResult.Failure($"ffmpeg could not write the matched clip (exit {apply.ExitCode})");
        }

        var verified = await verifier.AnalyzeAsync(temp, ct);
        if (!verified.Measurable)
        {
            return LoudnessMatchResult.Failure("matched clip could not be measured");
        }

        if (Math.Abs(verified.IntegratedLufs - TargetLufs) > ToleranceLu)
        {
            return LoudnessMatchResult.Failure(
                string.Create(CultureInfo.InvariantCulture, $"matched clip measures {verified.IntegratedLufs:0.0} LUFS, not {TargetLufs:0} ±{ToleranceLu:0}"),
                verified.IntegratedLufs);
        }

        File.Move(temp, output, overwrite: true);
        return LoudnessMatchResult.Success(verified.IntegratedLufs, LoudnormMeasurement.IsLinear(apply.Stderr));
    }
}
