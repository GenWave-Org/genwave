using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Wraps a probe and throws (<paramref name="failure"/>, default an InvalidOperationException) on chosen reads (zero-based, per probe kind); every other read goes through.</summary>
internal sealed class ThrowingResourceProbe(IResourceProbe inner, int containerRead = -1, int gpuRead = -1, Exception? failure = null) : IResourceProbe
{
    private int containerReads;
    private int gpuReads;

    public Task<string?> ReadContainerMemoryAsync(string container, CancellationToken ct)
    {
        int index = containerReads++;
        // The inner probe is still asked, so a script that ends the window by counting reads keeps counting.
        Task<string?> answer = inner.ReadContainerMemoryAsync(container, ct);
        return index == containerRead ? throw (failure ?? new InvalidOperationException("probe blew up")) : answer;
    }

    public Task<string?> ReadContainerPidsAsync(string container, CancellationToken ct) => inner.ReadContainerPidsAsync(container, ct);

    public Task<string?> ReadGpuProcessesAsync(CancellationToken ct)
    {
        int index = gpuReads++;
        Task<string?> answer = inner.ReadGpuProcessesAsync(ct);
        return index == gpuRead ? throw (failure ?? new InvalidOperationException("probe blew up")) : answer;
    }
}
