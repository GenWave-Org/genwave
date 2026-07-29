namespace GenWave.Host.Stats;

/// <summary>
/// The standard docker CPU-percentage formula (gh-#148), the same math the `docker stats` CLI
/// applies to the two samples a one-shot stats read carries:
/// <code>
///   cpuDelta    = cpu_stats.cpu_usage.total_usage − precpu_stats.cpu_usage.total_usage
///   systemDelta = cpu_stats.system_cpu_usage      − precpu_stats.system_cpu_usage
///   cpu%        = (cpuDelta / systemDelta) × cpu_stats.online_cpus × 100
/// </code>
/// Both counters are cumulative nanoseconds, so the two deltas share a time base and their ratio
/// is this container's share of ALL host cpu time over the sample window; × online_cpus rescales
/// to docker's per-core convention (a container saturating 2 of 8 cores reads 200%, not 25%).
/// </summary>
public static class DockerCpuCalculator
{
    /// <summary>
    /// Null — never a fabricated 0 — when the percentage cannot honestly be computed: a missing
    /// sample or counter, the first-sample edge (a zeroed <c>precpu_stats</c>, i.e.
    /// <c>system_cpu_usage</c> 0/absent), a zero or negative system delta (identical samples, or
    /// counters that went backwards across a daemon restart), a backwards cpu counter, or an
    /// unknown cpu count. A genuinely idle container still computes: cpuDelta 0 over a positive
    /// systemDelta is an honest 0%.
    /// </summary>
    public static double? CpuPercent(DockerCpuStats? current, DockerCpuStats? previous)
    {
        var currentTotal = current?.CpuUsage?.TotalUsage;
        var previousTotal = previous?.CpuUsage?.TotalUsage;
        var currentSystem = current?.SystemCpuUsage;
        var previousSystem = previous?.SystemCpuUsage;

        if (currentTotal is null || previousTotal is null || currentSystem is null || previousSystem is null)
            return null;

        // First-sample edge: docker zeroes precpu_stats when there is no previous sample. A real
        // host's cumulative cpu counter is never 0, so 0 here means "no previous sample", not data.
        if (previousSystem.Value == 0)
            return null;

        // Unsigned counters — compare before subtracting, or a backwards counter wraps huge.
        if (currentSystem.Value <= previousSystem.Value || currentTotal.Value < previousTotal.Value)
            return null;

        var onlineCpus = current?.OnlineCpus ?? previous?.OnlineCpus;
        if (onlineCpus is null || onlineCpus.Value <= 0)
            return null;

        var cpuDelta = (double)(currentTotal.Value - previousTotal.Value);
        var systemDelta = (double)(currentSystem.Value - previousSystem.Value);
        return cpuDelta / systemDelta * onlineCpus.Value * 100.0;
    }
}
