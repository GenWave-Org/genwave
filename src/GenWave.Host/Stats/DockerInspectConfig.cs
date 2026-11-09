namespace GenWave.Host.Stats;

using System.Text.Json.Serialization;

/// <summary>The <c>Config</c> slice of a docker inspect payload: just the labels (gh-#629).</summary>
public sealed record DockerInspectConfig
{
    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string>? Labels { get; init; }
}
