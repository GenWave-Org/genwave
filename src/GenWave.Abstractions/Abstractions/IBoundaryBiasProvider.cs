namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F74.3 (STORY-198, PLAN T43) — the thin accessor seam between <c>GenWave.Orchestration</c>
/// (which references only <c>GenWave.Core</c> and cannot see the Host's
/// <c>IOptionsMonitor&lt;T&gt;</c> / <c>StationOptions</c>) and the Host's boundary-bias
/// configuration. Mirrors <see cref="IRenderBudgetProvider"/> one seam over: a single
/// <see cref="TimeSpan"/> knob, read fresh on every call rather than cached.
///
/// <para>
/// Unlike <see cref="ICadenceProvider"/>/<see cref="IRotationSettingsProvider"/>/
/// <see cref="IRenderBudgetProvider"/>, this knob is deliberately NOT joined to the
/// <c>PUT /api/settings</c> live-edit allowlist for v1 — it is a boot/env-tunable value only
/// (soft, subordinate tuning; no operator urgency for a live write path yet). The Host
/// implementation still wraps <c>IOptionsMonitor&lt;StationOptions&gt;</c> so a config-provider
/// reload is picked up without a process restart, same as every sibling provider.
/// </para>
/// </summary>
public interface IBoundaryBiasProvider
{
    /// <summary>
    /// How far ahead of a pending deferral's due time track selection starts biasing toward a
    /// track whose end lands near that due time, evaluated fresh on every call. Zero disables the
    /// bias entirely — every selection takes the plain, unbiased path.
    /// </summary>
    TimeSpan Current { get; }
}
