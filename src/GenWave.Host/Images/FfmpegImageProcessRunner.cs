using GenWave.Loudness;

namespace GenWave.Host.Images;

/// <summary>
/// The production <see cref="IImageProcessRunner"/> — delegates straight to the house
/// argv-only/no-shell ffmpeg plumbing (PLAN T284's consolidation) rather than rolling a second
/// <see cref="System.Diagnostics.Process"/>-invocation shape. No behavior of its own beyond that
/// delegation; its entire reason to exist is being the thing <see cref="ImageNormalizeService"/>
/// depends on instead of <see cref="FfmpegProcess"/> directly, so tests can substitute a fake in
/// its place (see <see cref="IImageProcessRunner"/>'s own remarks).
/// </summary>
internal sealed class FfmpegImageProcessRunner : IImageProcessRunner
{
    public Task RunAsync(IReadOnlyList<string> args, CancellationToken ct) =>
        FfmpegProcess.RunFfmpegAsync(args, ct);
}
