namespace GenWave.Host.Stats;

using System.Text.Json.Serialization;

/// <summary>The <c>memory_stats.stats</c> detail object — see <see cref="DockerMemoryStats"/>.</summary>
public sealed record DockerMemoryDetailStats
{
    /// <summary>Reclaimable page cache in bytes (cgroup v2's <c>inactive_file</c>).</summary>
    [JsonPropertyName("inactive_file")]
    public long? InactiveFile { get; init; }
}
