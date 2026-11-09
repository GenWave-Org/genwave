namespace GenWave.Host.Stats;

using System.Text.Json.Serialization;

/// <summary>
/// The slice of <c>GET /containers/{id}/json</c> (inspect) the Health page reads (gh-#148):
/// restart count + health verdict. Allowlisted by the same <c>CONTAINERS=1</c> grant as the list
/// and stats calls — no extra proxy permission involved.
/// </summary>
public sealed record DockerContainerInspect
{
    [JsonPropertyName("RestartCount")]
    public int? RestartCount { get; init; }

    [JsonPropertyName("State")]
    public DockerInspectState? State { get; init; }

    /// <summary>Container config; only its labels are read (gh-#629 self-inspect).</summary>
    [JsonPropertyName("Config")]
    public DockerInspectConfig? Config { get; init; }
}
