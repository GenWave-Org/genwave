using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Options;

/// <summary>
/// Background dependency-probe cadence (SPEC F70.2, STORY-187): how often
/// <c>DependencyHealthProbeService</c> re-checks Ollama/Kokoro, and the per-probe timeout budget.
/// Deployment tuning, not operator-editable station config — deliberately absent from
/// <c>StationSettingsAllowlist</c>, the same exclusion shape as <c>IcecastOptions</c>/
/// <c>SpectatorOptions</c>.
/// </summary>
public sealed class DependencyHealthOptions
{
    public const string SectionName = "DependencyHealth";

    /// <summary>Seconds between probe cycles (SPEC F70.2 AC1).</summary>
    [Range(1, int.MaxValue)]
    public int ProbeIntervalSeconds { get; set; } = 30;

    /// <summary>Per-probe budget in seconds before it counts as a timeout (SPEC F70.2 AC3).</summary>
    [Range(1, int.MaxValue)]
    public int ProbeTimeoutSeconds { get; set; } = 5;
}
