namespace GenWave.Host.Api;

/// <summary>
/// One container row of <c>GET /api/health/containers</c> (gh-#148). Every measurement is
/// nullable and null means "unknown", never 0 — the F62.5 "never fabricated" discipline: a failed
/// stats read must not render as an idle container.
/// </summary>
/// <param name="Name">Compose service name when the container carries the compose label
/// (<c>api</c>, <c>engine</c>, …), otherwise its docker name without the leading slash.</param>
/// <param name="State">Docker lifecycle state: <c>running</c>/<c>restarting</c>/<c>exited</c>/….</param>
/// <param name="Health">Healthcheck verdict (<c>healthy</c>/<c>unhealthy</c>/<c>starting</c>);
/// null when the image defines no healthcheck or inspect degraded.</param>
/// <param name="CpuPercent">Per-core-scaled cpu percentage (<see cref="GenWave.Host.Stats.DockerCpuCalculator"/>);
/// null for a non-running container or an uncomputable sample.</param>
/// <param name="MemoryUsedBytes">Usage minus reclaimable page cache — `docker stats`' figure.</param>
/// <param name="MemoryLimitBytes">The container's cap, or the host total when uncapped.</param>
/// <param name="RestartCount">Docker's restart counter; null when inspect degraded. Monotonic —
/// never decays on its own, only a container recreation resets it — so pair it with
/// <paramref name="StartedAt"/> before treating it as a live incident (gh-#490).</param>
/// <param name="StartedAt">ISO 8601 timestamp of when the current container instance last
/// started — Docker's proxy for "when did the last restart happen"; null when inspect degraded.
/// Lets the Health page tell a historical restart storm from a current one.</param>
public sealed record ContainerStatDto(
    string Name,
    string State,
    string? Health,
    double? CpuPercent,
    long? MemoryUsedBytes,
    long? MemoryLimitBytes,
    int? RestartCount,
    string? StartedAt);
