namespace GenWave.Host.Images;

/// <summary>
/// The one seam <see cref="ImageNormalizeService"/> invokes ffmpeg through (PLAN T291). Production
/// wires <see cref="FfmpegImageProcessRunner"/> (a thin wrapper over
/// <see cref="GenWave.Loudness.FfmpegProcess"/>); tests substitute a counting/observing fake so the
/// "gates run BEFORE any decoder/ffmpeg touches the bytes" specs can prove ffmpeg was never
/// invoked at all on a rejected input, not merely that the service returned the right failure
/// reason. Public (rather than internal, like <see cref="FfmpegImageProcessRunner"/> itself)
/// purely so <see cref="ImageNormalizeService"/>'s own public constructor can take it — the
/// built-in DI container only ever resolves a service type's PUBLIC constructor.
/// </summary>
public interface IImageProcessRunner
{
    /// <summary>Runs ffmpeg with <paramref name="args"/> (argv-only), throwing on a non-zero exit
    /// or a cancelled/timed-out run — same contract as
    /// <see cref="GenWave.Loudness.FfmpegProcess.RunFfmpegAsync"/>, which
    /// <see cref="FfmpegImageProcessRunner"/> delegates to directly.</summary>
    Task RunAsync(IReadOnlyList<string> args, CancellationToken ct);
}
