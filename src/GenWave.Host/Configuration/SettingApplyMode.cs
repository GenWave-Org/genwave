namespace GenWave.Host.Configuration;

/// <summary>
/// Governs how a stored setting takes effect after it is written.
/// </summary>
public enum SettingApplyMode
{
    /// <summary>
    /// The new value is observable through <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>
    /// immediately after the provider reload — no process or engine restart required.
    /// </summary>
    Live,

    /// <summary>
    /// The new value is stored and will surface in configuration after the next engine (Liquidsoap)
    /// restart. The control plane reads it on startup via the env-var it maps to.
    /// </summary>
    EngineRestart,

    /// <summary>
    /// The new value is observable through <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>
    /// immediately, but only changes behavior the next time a file is (re-)analyzed — the analyzer
    /// (<c>ICueAnalyzer</c>/<c>IEnergyAnalyzer</c>) reads it per <c>AnalyzeAsync</c> call, not
    /// continuously, so an already-enriched row is unaffected until it is re-enriched (SPEC F44.3).
    /// </summary>
    Enrichment,
}
