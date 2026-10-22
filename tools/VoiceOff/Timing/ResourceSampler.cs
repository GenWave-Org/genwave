namespace GenWave.VoiceOff.Timing;

/// <summary>
/// Samples an engine container's memory and the GPU's memory every second until cancelled (SPEC F214.7).
/// Peak RSS is the largest <c>docker stats</c> reading; peak VRAM is the largest GPU-total reading minus the
/// baseline read once before the first sample. A probe that fails for one sample is skipped; timing never fails a run.
/// </summary>
internal sealed class ResourceSampler(IResourceProbe probe, string container, TimeProvider time)
{
    internal static readonly TimeSpan Cadence = TimeSpan.FromSeconds(1);

    private readonly Lock gate = new();
    private double? peakRssMib;
    private long? peakVramMib;
    private long? baselineMib;

    /// <summary>The peaks so far; read it after <see cref="RunAsync"/> returns for the final figures.</summary>
    internal ResourceUsage Usage
    {
        get
        {
            lock (gate)
            {
                return new ResourceUsage(
                    peakRssMib is double rss ? Megabytes.FromMib(rss) : null,
                    peakVramMib is long vram && baselineMib is long baseline ? NvidiaSmiParser.DeltaMb(baseline, vram) : null);
            }
        }
    }

    /// <summary>Samples now and then every <see cref="Cadence"/> until <paramref name="ct"/> is cancelled, which is the normal way to stop; it returns, it does not throw.</summary>
    internal async Task RunAsync(CancellationToken ct)
    {
        try
        {
            long? baseline = NvidiaSmiParser.ParseUsedMib(await TryReadAsync(() => probe.ReadGpuMemoryAsync(ct)));
            lock (gate)
            {
                baselineMib = baseline;
            }

            using PeriodicTimer timer = new(Cadence, time);
            do
            {
                await SampleAsync(ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // Cancellation ends the window.
        }
    }

    private async Task SampleAsync(CancellationToken ct)
    {
        double? rss = DockerStatsParser.ParseMib(await TryReadAsync(() => probe.ReadContainerMemoryAsync(container, ct)));
        long? vram = NvidiaSmiParser.ParseUsedMib(await TryReadAsync(() => probe.ReadGpuMemoryAsync(ct)));
        lock (gate)
        {
            if (rss is double r)
            {
                peakRssMib = Math.Max(peakRssMib ?? r, r);
            }

            if (vram is long v)
            {
                peakVramMib = Math.Max(peakVramMib ?? v, v);
            }
        }
    }

    /// <summary>One probe read; a probe that throws gives no sample. Cancellation still ends the window.</summary>
    private static async Task<string?> TryReadAsync(Func<Task<string?>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
