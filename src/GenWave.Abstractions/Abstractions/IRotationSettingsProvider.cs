using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F41.6 — the thin accessor seam between <c>GenWave.Orchestration</c> / <c>GenWave.Core.Playout</c>
/// (which reference only <c>GenWave.Core</c> and cannot see the Host's <c>IOptionsMonitor&lt;T&gt;</c>
/// / <c>StationOptions</c>) and the Host's live configuration. Mirrors <see cref="ICadenceProvider"/>/
/// <see cref="IStationScopeProvider"/> one seam over: <c>Station:Rotation:*</c> is advertised
/// <c>Live</c> in the settings allowlist, so a boot-frozen <see cref="RotationSettings"/> on
/// <see cref="Core.Playout.PlayoutFeeder"/> or the Orchestrator would be the same class of bug F30/gitea-#211
/// fixed for scope and cadence.
///
/// <para>
/// Implementations MUST re-evaluate <see cref="Current"/> on every read — never cache the result
/// in a field — so a live rotation edit is visible to the very next ring write / selection pass with
/// no process restart. The Host implementation wraps <c>IOptionsMonitor&lt;StationOptions&gt;</c>,
/// which already is the cache; this interface adds nothing beyond a Core-visible accessor.
/// </para>
/// </summary>
public interface IRotationSettingsProvider
{
    /// <summary>The station's current rotation knobs, evaluated fresh on every call.</summary>
    RotationSettings Current { get; }
}
