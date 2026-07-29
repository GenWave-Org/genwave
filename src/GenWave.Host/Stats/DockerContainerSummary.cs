namespace GenWave.Host.Stats;

using System.Text.Json.Serialization;

/// <summary>
/// One row of the Docker Engine API's <c>GET /containers/json</c> (gh-#148) — only the fields the
/// Health page needs; everything else in the payload is ignored on deserialization. Field casing
/// is Docker's own PascalCase, pinned by attribute so a serializer-default change can never
/// silently unmap them.
/// </summary>
public sealed record DockerContainerSummary
{
    [JsonPropertyName("Id")]
    public string Id { get; init; } = "";

    /// <summary>Docker names carry a leading slash (<c>"/genwave-api-1"</c>).</summary>
    [JsonPropertyName("Names")]
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>Lifecycle state: <c>running</c>/<c>restarting</c>/<c>exited</c>/<c>paused</c>/….</summary>
    [JsonPropertyName("State")]
    public string State { get; init; } = "";

    /// <summary>
    /// Container labels — <c>com.docker.compose.service</c> is how a compose-managed container
    /// names its service without prefix/suffix stripping.
    /// </summary>
    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
}
