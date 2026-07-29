namespace GenWave.Host.Stats;

using System.Text.Json.Serialization;

/// <summary>
/// The slice of <c>GET /containers/{id}/stats?stream=false</c> the Health page reads (gh-#148).
/// A one-shot (<c>stream=false</c>) read makes the daemon take TWO cpu samples ~1s apart —
/// <c>precpu_stats</c> is the earlier one — which is exactly what
/// <see cref="DockerCpuCalculator"/>'s percentage needs from a single request (verified live
/// against Docker 29.2.1 through the pinned socket-proxy: the call blocks ~1s and returns a
/// populated <c>precpu_stats</c>). Stats-endpoint casing is Docker's snake_case, unlike the
/// PascalCase list/inspect payloads — attribute-pinned for the same reason as
/// <see cref="DockerContainerSummary"/>.
/// </summary>
public sealed record DockerContainerStats
{
    [JsonPropertyName("cpu_stats")]
    public DockerCpuStats? CpuStats { get; init; }

    /// <summary>The previous cpu sample. Zero-valued (not absent) when no previous sample exists —
    /// the first-sample edge <see cref="DockerCpuCalculator.CpuPercent"/> reports as null.</summary>
    [JsonPropertyName("precpu_stats")]
    public DockerCpuStats? PreCpuStats { get; init; }

    [JsonPropertyName("memory_stats")]
    public DockerMemoryStats? MemoryStats { get; init; }
}
