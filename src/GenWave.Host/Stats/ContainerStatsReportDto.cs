namespace GenWave.Host.Stats;

/// <summary>
/// The <c>GET /api/health/containers</c> envelope (gh-#148). Always 200: when the docker-stats
/// sidecar is unreachable (or unconfigured) this is <c>{ degraded: true, reason, containers: [] }</c>
/// — the Health page renders "stats unavailable" from it, never an error state (fail-safe, the
/// same posture as every other degrading read surface).
/// </summary>
/// <param name="Degraded">True when the sidecar could not be consulted at all.</param>
/// <param name="Reason">Human-readable cause, only when <paramref name="Degraded"/>.</param>
/// <param name="Containers">Per-container rows, name-sorted; empty when degraded.</param>
public sealed record ContainerStatsReportDto(
    bool Degraded,
    string? Reason,
    IReadOnlyList<ContainerStatDto> Containers);
