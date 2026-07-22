using GenWave.Abstractions.Playout;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F81.1/F81.3 — the thin accessor seam between <c>GenWave.Orchestration</c> (which references
/// only <c>GenWave.Core</c> and cannot see the Host's <c>IOptionsMonitor&lt;StationOptions&gt;</c>)
/// and the Host's live <c>Station:Envelope:*</c> configuration. Mirrors
/// <see cref="IBoundaryBiasProvider"/> one seam over: a single value read fresh on every call rather
/// than cached.
///
/// <para>
/// Implementations MUST re-evaluate <see cref="Current"/> on every call — never cache the result in
/// a field — so a live <c>PUT /api/settings</c> edit to <c>Station:Envelope:Genres</c>/<c>EnergyMin</c>/
/// <c>EnergyMax</c> reaches the very next pick with no process restart (the same F30.1/gitea-#211
/// discipline every sibling provider — <see cref="IRotationSettingsProvider"/>,
/// <see cref="IBoundaryBiasProvider"/>, <see cref="ICadenceProvider"/> — follows).
/// </para>
///
/// <para>
/// v1 ships exactly one 24/7 station-default envelope (SPEC F81.3) — <see cref="Current"/> always
/// resolves the SAME envelope regardless of wall-clock time; <see cref="SegmentEnvelope.StartsAt"/>/
/// <see cref="SegmentEnvelope.EndsAt"/> are not consulted by any implementation today.
/// </para>
/// </summary>
public interface IEnvelopeProvider
{
    /// <summary>The station's current segment envelope, evaluated fresh on every call.</summary>
    SegmentEnvelope Current { get; }
}
