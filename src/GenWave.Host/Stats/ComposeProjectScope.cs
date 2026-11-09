namespace GenWave.Host.Stats;

/// <summary>
/// Singleton memory for the api's own compose project (gh-#629), so the Health roster learns it
/// once per process instead of per poll (<see cref="DockerContainerStatsSource"/> is a transient
/// typed client and cannot hold it). Only a settled answer is kept: a lookup returning null
/// (sidecar unreachable, 5xx) is retried on the next poll.
/// </summary>
public sealed class ComposeProjectScope
{
    readonly SemaphoreSlim gate = new(1, 1);
    volatile ComposeProjectLookup? settled;

    public async Task<string?> GetAsync(Func<CancellationToken, Task<ComposeProjectLookup?>> lookup, CancellationToken ct)
    {
        if (settled is { } known)
            return known.Name;

        await gate.WaitAsync(ct);
        try
        {
            settled ??= await lookup(ct);
            return settled?.Name;
        }
        finally
        {
            gate.Release();
        }
    }
}
