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

    /// <summary>When the current container instance last started — Docker's own proxy for "last
    /// restart" (gh-#490): <c>RestartCount</c> is monotonic and never decays, so pairing it with
    /// this timestamp is what lets the Health page tell a live crash loop from a long-since-fixed
    /// one. ISO 8601, passed through as-is; the reader parses it.</summary>
    [JsonPropertyName("StartedAt")]
    public string? StartedAt { get; init; }
}
