namespace GenWave.Host.Stats;

using System.Text.Json.Serialization;

/// <summary>A cpu sample's <c>cpu_usage</c> object — see <see cref="DockerCpuStats"/>.</summary>
public sealed record DockerCpuUsage
{
    /// <summary>This container's cumulative cpu time in nanoseconds (uint64 on the wire).</summary>
    [JsonPropertyName("total_usage")]
    public ulong? TotalUsage { get; init; }
}
