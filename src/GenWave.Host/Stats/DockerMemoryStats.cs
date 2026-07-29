namespace GenWave.Host.Stats;

using System.Text.Json.Serialization;

/// <summary>The stats payload's <c>memory_stats</c> object — see <see cref="DockerContainerStats"/>.</summary>
public sealed record DockerMemoryStats
{
    /// <summary>Raw cgroup usage in bytes — includes page cache; the honest "used" figure
    /// subtracts <see cref="DockerMemoryDetailStats.InactiveFile"/>, exactly as `docker stats` does
    /// (see <see cref="DockerContainerStatsSource"/>).</summary>
    [JsonPropertyName("usage")]
    public long? Usage { get; init; }

    /// <summary>The container's memory limit in bytes — the host's total when uncapped.</summary>
    [JsonPropertyName("limit")]
    public long? Limit { get; init; }

    [JsonPropertyName("stats")]
    public DockerMemoryDetailStats? Stats { get; init; }
}
