namespace GenWave.Host.Stats;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Host.Options;

/// <summary>
/// Reads container-level stats for the admin Health page (gh-#148) from the allowlisted
/// docker-socket-proxy sidecar (<c>DockerStats:BaseUrl</c>, compose service <c>dockerproxy</c>):
/// <c>GET /containers/json?all=true</c> for the roster, then per running container one one-shot
/// <c>GET /containers/{id}/stats?stream=false</c> (cpu + memory — see
/// <see cref="DockerCpuCalculator"/> for the percentage math) and one
/// <c>GET /containers/{id}/json</c> (health verdict + restart count + last-start time; also the
/// one-time self-inspect that learns the compose project). All paths sit behind the proxy's single <c>CONTAINERS=1</c> grant.
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
/// <para>
/// Utility containers are invisible (gh-#283): a compose service labeled
/// <c>genwave.role: utility</c> (ollama-init today — one-shot/ephemeral helpers generally) is
/// dropped from the roster before any per-container read, so every consumer of the report
/// (health cards, admin UI) inherits the exclusion. The label is the contract — new helpers get
/// the label in compose, never a name hardcoded here.
/// </para>
/// <para>
/// The roster is scoped to the station's own compose project (gh-#629): the project is
/// <c>DockerStats:ComposeProject</c> / <c>COMPOSE_PROJECT_NAME</c> when set, else the
/// <c>com.docker.compose.project</c> label of this api's own container (inspected by hostname,
/// which in a container is its id), and the listing is filtered server-side with docker's
/// <c>filters</c> param. <c>label=k=v</c> is exact equality, so a look-alike project such as
/// <c>genwave-t434c-…</c> never matches <c>genwave</c>. If the project cannot be determined the
/// roster lists every container (the pre-#629 behaviour), logged once at Information.
/// </para>
/// </summary>
public sealed class DockerContainerStatsSource(
    HttpClient http,
    IOptionsMonitor<DockerStatsOptions> optionsMonitor,
    ComposeProjectScope projectScope,
    ILogger<DockerContainerStatsSource> logger)
{
    const string ComposeServiceLabel = "com.docker.compose.service";
    const string RoleLabel = "genwave.role";
    const string UtilityRole = "utility";
    const string ComposeProjectLabel = "com.docker.compose.project";

    public async Task<ContainerStatsReportDto> GetReportAsync(CancellationToken ct)
    {
        var baseUrl = optionsMonitor.CurrentValue.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl))
            return new ContainerStatsReportDto(true, "Container stats are not configured (DockerStats:BaseUrl is empty).", []);

        IReadOnlyList<DockerContainerSummary> summaries;
        try
        {
            var project = await projectScope.GetAsync(lookupCt => LookUpProjectAsync(baseUrl, lookupCt), ct);
            summaries = await http.GetFromJsonAsync<IReadOnlyList<DockerContainerSummary>>(
                BuildUri(baseUrl, ListPath(project)), ct) ?? [];
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

        var rows = await Task.WhenAll(summaries
            .Where(summary => !IsUtility(summary))
            .Select(summary => BuildRowAsync(baseUrl, summary, ct)));
        return new ContainerStatsReportDto(false, null, rows.OrderBy(row => row.Name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The roster listing path. With a project it carries docker's <c>filters</c> param
    /// (<c>{"label":["com.docker.compose.project=&lt;name&gt;"]}</c>, URL-encoded); without one it is
    /// the unfiltered pre-#629 listing. Public static so the exact request is spec-pinned.
    /// </summary>
    public static string ListPath(string? project)
    {
        if (string.IsNullOrEmpty(project))
            return "/containers/json?all=true";

        var filters = JsonSerializer.Serialize(new Dictionary<string, string[]>
        {
            ["label"] = [$"{ComposeProjectLabel}={project}"],
        });
        return $"/containers/json?all=true&filters={Uri.EscapeDataString(filters)}";
    }

    /// <summary>
    /// Learns the project once (see <see cref="ComposeProjectScope"/>): configured value first,
    /// then this container's own compose label. A definitive answer (including "no label" and a
    /// 403/404 from the proxy) settles; a transport failure or 5xx returns null so the next poll
    /// retries, listing everything meanwhile.
    /// </summary>
    async Task<ComposeProjectLookup?> LookUpProjectAsync(string baseUrl, CancellationToken ct)
    {
        var configured = optionsMonitor.CurrentValue.ComposeProject;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            logger.LogInformation("Health roster scoped to configured compose project {ComposeProject}", configured);
            return new ComposeProjectLookup(configured);
        }

        try
        {
            var self = await http.GetFromJsonAsync<DockerContainerInspect>(
                BuildUri(baseUrl, $"/containers/{Uri.EscapeDataString(Environment.MachineName)}/json"), ct);
            if (self?.Config?.Labels is { } labels &&
                labels.TryGetValue(ComposeProjectLabel, out var project) && !string.IsNullOrEmpty(project))
            {
                logger.LogInformation("Health roster scoped to own compose project {ComposeProject}", project);
                return new ComposeProjectLookup(project);
            }

            logger.LogInformation("Own container carries no compose project label; Health roster lists every container on the socket");
            return new ComposeProjectLookup(null);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            logger.LogInformation("Own container is not inspectable ({Status}); Health roster lists every container on the socket", ex.StatusCode);
            return new ComposeProjectLookup(null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Own compose project lookup failed; listing every container this poll and retrying next");
            return null;
        }
    }

    /// <summary>
    /// The gh-#283 exclusion contract: a container labeled <c>genwave.role: utility</c> is a
    /// one-shot/ephemeral helper (compose labels ride into <c>/containers/json</c>'s
    /// <c>Labels</c>), so the Health page never lists it — an exited ollama-init is routine, not
    /// a red card. Value match is case-insensitive, same tolerance as the running-state check.
    /// Public static so the contract is spec-pinned directly.
    /// </summary>
    public static bool IsUtility(DockerContainerSummary summary) =>
        summary.Labels.TryGetValue(RoleLabel, out var role) &&
        string.Equals(role, UtilityRole, StringComparison.OrdinalIgnoreCase);

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
