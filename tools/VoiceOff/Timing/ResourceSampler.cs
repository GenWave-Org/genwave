namespace GenWave.VoiceOff.Timing;

/// <summary>
/// Samples an engine container's memory, and on the GPU tier its GPU memory, every second until cancelled (SPEC F214.7).
/// Peak RSS is the largest <c>docker stats</c> reading. Peak VRAM is the largest per-sample sum of the GPU memory used by
/// the container's own processes. On the CPU tier VRAM is never read and stays null. A probe that fails for one sample
/// is skipped; timing never fails a run.
/// </summary>
internal sealed class ResourceSampler(IResourceProbe probe, string container, TimeProvider time, bool readGpu = false)
{
    internal static readonly TimeSpan Cadence = TimeSpan.FromSeconds(1);

    private readonly Lock gate = new();
    private double? peakRssMib;
    private long? peakVramMib;

    /// <summary>The peaks so far; read it after <see cref="RunAsync"/> returns for the final figures.</summary>
    internal ResourceUsage Usage
    {
        get
        {
            lock (gate)
            {
                return new ResourceUsage(peakRssMib is double rss ? Megabytes.FromMib(rss) : null, peakVramMib);
            }
        }
    }

    /// <summary>Samples now and then every <see cref="Cadence"/> until <paramref name="ct"/> is cancelled, which is the normal way to stop; it returns, it does not throw.</summary>
    internal async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using PeriodicTimer timer = new(Cadence, time);
            do
            {
                await SampleAsync(ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancellation ends the window. A probe's own timeout is not cancellation, so it does not reach here.
        }
    }

    private async Task SampleAsync(CancellationToken ct)
    {
        double? rss = DockerStatsParser.ParseMib(await TryReadAsync(() => probe.ReadContainerMemoryAsync(container, ct), ct));
        long? vram = readGpu ? await ReadVramAsync(ct) : null;
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

    private async Task<long?> ReadVramAsync(CancellationToken ct)
    {
        IReadOnlySet<int>? pids = NvidiaSmiParser.ParsePids(await TryReadAsync(() => probe.ReadContainerPidsAsync(container, ct), ct));
        if (pids is null)
        {
            return null;
        }

        string? gpu = await TryReadAsync(() => probe.ReadGpuProcessesAsync(ct), ct);
        return NvidiaSmiParser.ParseContainerUsedMib(gpu, pids);
    }

    /// <summary>One probe read; a probe that throws gives no sample. The caller's own cancellation still ends the window.</summary>
    private static async Task<string?> TryReadAsync(Func<Task<string?>> read, CancellationToken ct)
    {
        try
        {
            return await read();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
