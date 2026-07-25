using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Options;

/// <summary>
/// Background dependency-probe cadence (SPEC F70.2, STORY-187): how often
/// <c>DependencyHealthProbeService</c> re-checks Ollama/Kokoro, the per-probe timeout budget, and
/// how many consecutive failures it takes to conclude a dependency is down.
/// <para>
/// Originally excluded from <c>StationSettingsAllowlist</c> as "deployment tuning, not
/// operator-editable station config". gh-#125 reversed that: diagnosing a flapping probe on a live
/// station meant editing compose and redeploying to move a single number, twice. All three knobs
/// are now allowlisted and <see cref="GenWave.Host.Configuration.SettingApplyMode.Live"/> — the
/// prober re-reads them every cycle (see <c>DependencyProbeCadence</c>), so an edit lands on the
/// very next probe with no api restart.
/// </para>
/// </summary>
public sealed class DependencyHealthOptions
{
    public const string SectionName = "DependencyHealth";

    /// <summary>Seconds between probe cycles (SPEC F70.2 AC1).</summary>
    [Range(1, int.MaxValue)]
    public int ProbeIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Per-probe budget in seconds before it counts as a timeout (SPEC F70.2 AC3).
    /// <para>
    /// 10, not the original 5, since gh-#125. Kokoro serves <c>/health</c> from the same event loop
    /// it renders on and blocks it for the whole render — measured on the demo box at a median 6.9s
    /// and a p90 of 10.1s per text-split — so a 5s budget lost to a perfectly ordinary render and
    /// flapped the verdict ~25×/day. This value is the debounce's belt-and-braces partner, not a
    /// substitute for it: 10% of splits still exceed 10s, which is exactly what
    /// <see cref="UnhealthyThreshold"/> absorbs. Raising this alone can never close the gap without
    /// pushing the budget past half the interval and blinding the probe to a genuine outage.
    /// </para>
    /// </summary>
    [Range(1, int.MaxValue)]
    public int ProbeTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// How many probes must fail in a row before the cached verdict flips unhealthy and renders
    /// start routing to the fallback engine (SPEC F70.2 AC5, gh-#125). 1 restores the original
    /// flip-on-first-failure behavior.
    /// <para>
    /// 2 is the shipped default on direct evidence: across 298 probe timeouts in a 7-day demo-box
    /// window, ZERO landed on consecutive cycles — a render-length stall (~7s) is far shorter than
    /// the 30s interval, so two probes essentially never both land inside one. A threshold of 2
    /// would have suppressed all 298 spurious flips while still catching a genuinely dead
    /// dependency within 2 × <see cref="ProbeIntervalSeconds"/> (60s at the default cadence).
    /// </para>
    /// </summary>
    [Range(1, int.MaxValue)]
    public int UnhealthyThreshold { get; set; } = 2;
}
