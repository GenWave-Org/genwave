namespace GenWave.Host.Stats;

using System.Text.Json.Serialization;

/// <summary>One cpu sample (<c>cpu_stats</c>/<c>precpu_stats</c>) — see <see cref="DockerContainerStats"/>.</summary>
public sealed record DockerCpuStats
{
    [JsonPropertyName("cpu_usage")]
    public DockerCpuUsage? CpuUsage { get; init; }

    /// <summary>Host-wide cumulative cpu time in nanoseconds. Absent/zero in a zeroed
    /// <c>precpu_stats</c> (no previous sample) — uint64 on the wire, hence ulong.</summary>
    [JsonPropertyName("system_cpu_usage")]
    public ulong? SystemCpuUsage { get; init; }

    [JsonPropertyName("online_cpus")]
    public int? OnlineCpus { get; init; }
}
