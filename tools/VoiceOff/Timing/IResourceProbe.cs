namespace GenWave.VoiceOff.Timing;

/// <summary>Reads the raw text of one resource sample. Null means the probe could not answer this time.</summary>
internal interface IResourceProbe
{
    /// <summary>The <c>docker stats</c> memory-usage text for a container.</summary>
    Task<string?> ReadContainerMemoryAsync(string container, CancellationToken ct);

    /// <summary>The <c>nvidia-smi</c> memory-used text for all GPUs.</summary>
    Task<string?> ReadGpuMemoryAsync(CancellationToken ct);
}
