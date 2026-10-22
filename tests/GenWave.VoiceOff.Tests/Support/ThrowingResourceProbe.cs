using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Wraps a probe and throws on chosen reads (zero-based, per probe kind); every other read goes through.</summary>
internal sealed class ThrowingResourceProbe(IResourceProbe inner, int containerRead = -1, int gpuRead = -1) : IResourceProbe
{
    private int containerReads;
    private int gpuReads;

    public Task<string?> ReadContainerMemoryAsync(string container, CancellationToken ct)
    {
        int index = containerReads++;
        // The inner probe is still asked, so a script that ends the window by counting reads keeps counting.
        Task<string?> answer = inner.ReadContainerMemoryAsync(container, ct);
        return index == containerRead ? throw new InvalidOperationException("probe blew up") : answer;
    }

    public Task<string?> ReadGpuMemoryAsync(CancellationToken ct)
    {
        int index = gpuReads++;
        Task<string?> answer = inner.ReadGpuMemoryAsync(ct);
        return index == gpuRead ? throw new InvalidOperationException("probe blew up") : answer;
    }
}
