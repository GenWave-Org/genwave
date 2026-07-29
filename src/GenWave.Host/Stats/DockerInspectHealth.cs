namespace GenWave.Host.Stats;

using System.Text.Json.Serialization;

/// <summary>Inspect's <c>State.Health</c> object — <c>healthy</c>/<c>unhealthy</c>/<c>starting</c>.</summary>
public sealed record DockerInspectHealth
{
    [JsonPropertyName("Status")]
    public string? Status { get; init; }
}
