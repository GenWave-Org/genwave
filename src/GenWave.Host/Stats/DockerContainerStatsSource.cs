namespace GenWave.Host.Stats;

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Host.Api;
using GenWave.Host.Options;

/// <summary>
/// Reads container-level stats for the admin Health page (gh-#148) from the allowlisted
/// docker-socket-proxy sidecar (<c>DockerStats:BaseUrl</c>, compose service <c>dockerproxy</c>):
/// <c>GET /containers/json?all=true</c> for the roster, then per running container one one-shot
/// <c>GET /containers/{id}/stats?stream=false</c> (cpu + memory — see
/// <see cref="DockerCpuCalculator"/> for the percentage math) and one
/// <c>GET /containers/{id}/json</c> (health verdict + restart count + last-start time). All three
/// paths sit behind the proxy's single <c>CONTAINERS=1</c> grant.
/// <para>
/// Fail-safe throughout, the <see cref="IcecastListenerStatsSource"/> discipline: an unreachable
/// sidecar (or an empty <c>BaseUrl</c>) degrades to a well-formed
/// <c>{ degraded: true, reason, containers: [] }</c> — never an exception toward
/// <see cref="Api.HealthContainersController"/>, so the Health page renders "stats unavailable"
/// rather than erroring. A single container's failed stats/inspect read degrades to nulls on that
/// row only; the roster survives. The per-call budget is the typed client's 5s timeout
/// (Program.cs) — generous against the ~1s a one-shot stats read blocks by design (the daemon
/// takes the two cpu samples the percentage needs).
/// </para>
/// </summary>
public sealed class DockerContainerStatsSource(
    HttpClient http,
    IOptionsMonitor<DockerStatsOptions> optionsMonitor,
    ILogger<DockerContainerStatsSource> logger)
{
    const string ComposeServiceLabel = "com.docker.compose.service";

    public async Task<ContainerStatsReportDto> GetReportAsync(CancellationToken ct)
    {
        var baseUrl = optionsMonitor.CurrentValue.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl))
            return new ContainerStatsReportDto(true, "Container stats are not configured (DockerStats:BaseUrl is empty).", []);

        IReadOnlyList<DockerContainerSummary> summaries;
        try
        {
            summaries = await http.GetFromJsonAsync<IReadOnlyList<DockerContainerSummary>>(
                BuildUri(baseUrl, "/containers/json?all=true"), ct) ?? [];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // the caller aborted — not a sidecar failure to degrade
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Docker-stats sidecar unreachable; reporting a degraded empty container list");
            return new ContainerStatsReportDto(true, $"Container stats sidecar unreachable at {baseUrl}.", []);
        }

        var rows = await Task.WhenAll(summaries.Select(summary => BuildRowAsync(baseUrl, summary, ct)));
        return new ContainerStatsReportDto(false, null, rows.OrderBy(row => row.Name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// A compose-managed container names its service via the <c>com.docker.compose.service</c>
    /// label ("api", never "/genwave-api-1"); anything unlabeled falls back to its docker name
    /// with the leading slash stripped, then to a short id. Public static so the mapping is
    /// spec-pinned directly.
    /// </summary>
    public static string ResolveServiceName(DockerContainerSummary summary)
    {
        if (summary.Labels.TryGetValue(ComposeServiceLabel, out var service) && !string.IsNullOrEmpty(service))
            return service;

        var name = summary.Names.FirstOrDefault(candidate => !string.IsNullOrEmpty(candidate));
        if (name is not null)
            return name.TrimStart('/');

        return summary.Id.Length > 12 ? summary.Id[..12] : summary.Id;
    }

    /// <summary>
    /// `docker stats`' "used" figure: raw cgroup usage minus reclaimable page cache
    /// (<c>inactive_file</c>) — a media-heavy container (the engine streaming from /media) would
    /// otherwise read as tens of GiB of "use" that the kernel can drop at will.
    /// </summary>
    public static long? MemoryUsedBytes(DockerMemoryStats? memory)
    {
        var usage = memory?.Usage;
        if (usage is null)
            return null;

        var inactiveFile = memory?.Stats?.InactiveFile;
        if (inactiveFile is not null && inactiveFile.Value >= 0 && inactiveFile.Value <= usage.Value)
            return usage.Value - inactiveFile.Value;

        return usage.Value;
    }

    async Task<ContainerStatDto> BuildRowAsync(string baseUrl, DockerContainerSummary summary, CancellationToken ct)
    {
        var running = string.Equals(summary.State, "running", StringComparison.OrdinalIgnoreCase);

        // Stats first (it blocks ~1s for its two cpu samples), inspect after — both degrade to
        // null independently; a one-row failure must never take down the whole report.
        var stats = running ? await TryGetStatsAsync(baseUrl, summary.Id, ct) : null;
        var inspect = await TryInspectAsync(baseUrl, summary.Id, ct);

        return new ContainerStatDto(
            ResolveServiceName(summary),
            summary.State,
            inspect?.State?.Health?.Status,
            stats is null ? null : DockerCpuCalculator.CpuPercent(stats.CpuStats, stats.PreCpuStats),
            MemoryUsedBytes(stats?.MemoryStats),
            stats?.MemoryStats?.Limit,
            inspect?.RestartCount,
            inspect?.State?.StartedAt);
    }

    async Task<DockerContainerStats?> TryGetStatsAsync(string baseUrl, string id, CancellationToken ct)
    {
        try
        {
            return await http.GetFromJsonAsync<DockerContainerStats>(
                BuildUri(baseUrl, $"/containers/{Uri.EscapeDataString(id)}/stats?stream=false"), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "One-shot stats read failed for container {ContainerId}; row degrades to null cpu/memory", id);
            return null;
        }
    }

    async Task<DockerContainerInspect?> TryInspectAsync(string baseUrl, string id, CancellationToken ct)
    {
        try
        {
            return await http.GetFromJsonAsync<DockerContainerInspect>(
                BuildUri(baseUrl, $"/containers/{Uri.EscapeDataString(id)}/json"), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Inspect read failed for container {ContainerId}; row degrades to null health/restarts", id);
            return null;
        }
    }

    static Uri BuildUri(string baseUrl, string pathAndQuery) => new($"{baseUrl.TrimEnd('/')}{pathAndQuery}");
}
