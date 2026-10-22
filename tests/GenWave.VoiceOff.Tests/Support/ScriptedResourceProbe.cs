using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>
/// Answers probes from scripts, in order (a null entry is a failed probe). The first GPU answer is the baseline.
/// When the container script runs out it cancels the sampling window, so a run ends by itself.
/// </summary>
internal sealed class ScriptedResourceProbe(string?[] container, string?[] gpu, CancellationTokenSource window, List<string>? asked = null) : IResourceProbe
{
    private int containerReads;
    private int gpuReads;

    public Task<string?> ReadContainerMemoryAsync(string name, CancellationToken ct)
    {
        asked?.Add(name);
        string? answer = containerReads < container.Length ? container[containerReads] : null;
        containerReads++;
        if (containerReads >= container.Length)
        {
            window.Cancel();
        }

        return Task.FromResult(answer);
    }

    public Task<string?> ReadGpuMemoryAsync(CancellationToken ct)
    {
        string? answer = gpuReads < gpu.Length ? gpu[gpuReads] : null;
        gpuReads++;
        return Task.FromResult(answer);
    }

    /// <summary>Runs a sampler over this script on a manual clock, firing a tick whenever it waits. Bounded: a sampler that never stops fails the fact.</summary>
    internal static async Task<ResourceUsage> SampleAsync(string?[] container, string?[] gpu, ManualTimeProvider? clock = null, List<string>? asked = null, Func<IResourceProbe, IResourceProbe>? wrap = null)
    {
        using CancellationTokenSource window = new();
        ManualTimeProvider time = clock ?? new ManualTimeProvider();
        ScriptedResourceProbe scripted = new ScriptedResourceProbe(container, gpu, window, asked);
        ResourceSampler sampler = new(wrap?.Invoke(scripted) ?? scripted, "engine-box", time);
        Task run = sampler.RunAsync(window.Token);
        for (int i = 0; i < 5000 && !run.IsCompleted; i++)
        {
            time.FireAll();
            await Task.Delay(1);
        }

        await run.WaitAsync(TimeSpan.FromSeconds(5));
        return sampler.Usage;
    }
}
