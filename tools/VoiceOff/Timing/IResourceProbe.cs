namespace GenWave.VoiceOff.Timing;

/// <summary>Reads the raw text of one resource sample. Null means the probe could not answer this time.</summary>
internal interface IResourceProbe
{
    /// <summary>The <c>docker stats</c> memory-usage text for a container.</summary>
    Task<string?> ReadContainerMemoryAsync(string container, CancellationToken ct);

    /// <summary>The <c>docker top</c> process-id text for a container: host PIDs, one per line, under a <c>PID</c> header.</summary>
    Task<string?> ReadContainerPidsAsync(string container, CancellationToken ct);

    /// <summary>The <c>nvidia-smi</c> compute-apps text: one <c>pid, used_memory</c> line per process on any GPU.</summary>
    Task<string?> ReadGpuProcessesAsync(CancellationToken ct);
}
