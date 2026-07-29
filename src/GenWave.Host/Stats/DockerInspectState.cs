namespace GenWave.Host.Stats;

using System.Text.Json.Serialization;

/// <summary>Inspect's <c>State</c> object — see <see cref="DockerContainerInspect"/>.</summary>
public sealed record DockerInspectState
{
    [JsonPropertyName("Status")]
    public string? Status { get; init; }

    /// <summary>Null for a container whose image defines no healthcheck — that is the common case,
    /// not an error; the report's <c>health</c> field stays null for it.</summary>
    [JsonPropertyName("Health")]
    public DockerInspectHealth? Health { get; init; }
}
