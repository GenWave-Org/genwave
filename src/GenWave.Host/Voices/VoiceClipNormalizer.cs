using System.Globalization;
using GenWave.Core.Abstractions;
using GenWave.Loudness;

namespace GenWave.Host.Voices;

/// <summary>
/// Turns an uploaded recording into a reference clip (SPEC F219.12): mono, 24 kHz, 16-bit WAV at -20 LUFS. Two ffmpeg runs: decode
/// (demuxer forced, capped at <see cref="VoiceClipLimits.DecodeCapSeconds"/>) to a mono 24 kHz scratch file; measure it with
/// <see cref="ILoudnessAnalyzer"/> and read its length; then, if the length is in range, apply the gain that lands it on target
/// (with a limiter, auto-level off so the limiter cannot shift the loudness, so the boost can never clip). Every argument travels through <c>ArgumentList</c>, never a shell.
/// </summary>
public sealed class VoiceClipNormalizer(ILoudnessAnalyzer loudnessAnalyzer)
{
    /// <summary>
    /// Reads <paramref name="inputPath"/> (already known to be <paramref name="demuxer"/>) and writes the normalized clip to
    /// <paramref name="outputPath"/>. Leaves nothing at <paramref name="outputPath"/> unless it returns
    /// <see cref="VoiceClipNormalizeResult.Normalized"/>.
    /// </summary>
    public async Task<VoiceClipNormalizeResult> NormalizeAsync(
        string inputPath, string demuxer, string outputPath, CancellationToken ct)
    {
        var scratchPath = Path.Combine(Path.GetTempPath(), $"genwave-clip-{Guid.NewGuid():N}.wav");
        try
        {
            var decode = await FfmpegProcess.RunFfmpegCapturingStderrAsync(
                ["-nostdin", "-hide_banner", "-v", "error", "-t", VoiceClipLimits.DecodeCapSeconds.ToString(CultureInfo.InvariantCulture),
                 "-f", demuxer, "-i", inputPath, "-vn", "-ac", "1", "-ar", VoiceClipLimits.SampleRateHz.ToString(CultureInfo.InvariantCulture),
                 "-c:a", "pcm_s16le", "-f", "wav", "-y", scratchPath], ct);
            if (!decode.Succeeded || !File.Exists(scratchPath))
                return new VoiceClipNormalizeResult.Rejected("The file could not be read as audio.");

            // The scratch file is ours and just decoded cleanly, so a probe failure here is a server fault and propagates.
            var durationMs = (int)Math.Round(await FfmpegProcess.ProbeDurationSecondsAsync(scratchPath, ct) * 1000.0);
            if (durationMs < VoiceClipLimits.MinDurationMs)
                return new VoiceClipNormalizeResult.Rejected("The clip is shorter than 5 seconds.");
            if (durationMs > VoiceClipLimits.MaxDurationMs)
                return new VoiceClipNormalizeResult.Rejected("The clip is longer than 20 seconds.");

            var loudness = await loudnessAnalyzer.AnalyzeAsync(scratchPath, ct);
            if (!loudness.Measurable)
                return new VoiceClipNormalizeResult.Rejected("The clip is silent.");

            var gainDb = (VoiceClipLimits.TargetLufs - loudness.IntegratedLufs).ToString("F2", CultureInfo.InvariantCulture);
            var gain = await FfmpegProcess.RunFfmpegCapturingStderrAsync(
                ["-nostdin", "-hide_banner", "-v", "error", "-i", scratchPath, "-af", $"volume={gainDb}dB,alimiter=limit=0.95:level=0",
                 "-ac", "1", "-ar", VoiceClipLimits.SampleRateHz.ToString(CultureInfo.InvariantCulture),
                 "-c:a", "pcm_s16le", "-f", "wav", "-y", outputPath], ct);
            if (!gain.Succeeded)
            {
                File.Delete(outputPath);
                return new VoiceClipNormalizeResult.Rejected("The file could not be read as audio.");
            }

            return new VoiceClipNormalizeResult.Normalized(durationMs);
        }
        finally
        {
            File.Delete(scratchPath);
        }
    }
}
