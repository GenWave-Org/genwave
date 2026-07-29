namespace GenWave.Host.Options;

/// <summary>
/// Wiring for <see cref="Stats.DockerContainerStatsSource"/>'s container-stats reads (gh-#148).
/// Bound from the <c>DockerStats</c> config section — env/compose-only, like
/// <see cref="IcecastOptions"/>: deliberately absent from
/// <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/> (deployment topology, never
/// a live PUT).
/// </summary>
public sealed class DockerStatsOptions
{
    public const string SectionName = "DockerStats";

    /// <summary>
    /// Base URL of the allowlisted docker-socket-proxy sidecar (compose service
    /// <c>dockerproxy</c> on the <c>stats</c> network). The default matches compose.yaml's
    /// service name + the proxy image's default port, so a bare compose-up works with no env at
    /// all. Empty disables the feature: <see cref="Stats.DockerContainerStatsSource"/> reports a
    /// well-formed degraded response rather than throwing — same never-500 posture as an
    /// unreachable sidecar.
    /// </summary>
    public string BaseUrl { get; init; } = "http://dockerproxy:2375";
}
