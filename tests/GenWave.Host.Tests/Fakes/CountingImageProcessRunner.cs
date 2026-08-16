using GenWave.Host.Images;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// Counts invocations without ever touching ffmpeg — the seam Story333's "before ffmpeg" specs use
/// to PROVE a rejected input never reached <see cref="IImageProcessRunner"/> at all, not merely
/// that <see cref="ImageNormalizeService"/> returned the expected failure reason.
/// </summary>
sealed class CountingImageProcessRunner : IImageProcessRunner
{
    public int InvocationCount { get; private set; }

    public Task RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        InvocationCount++;
        return Task.CompletedTask;
    }
}
