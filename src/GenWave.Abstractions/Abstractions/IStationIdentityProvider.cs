using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F44.1 (gitea-#196) — the thin accessor seam between <c>GenWave.Orchestration</c> (which
/// references only <c>GenWave.Core</c> and cannot see the Host's <c>IOptionsMonitor&lt;T&gt;</c>
/// / <c>StationOptions</c>) and the Host's live configuration. Mirrors <see cref="ICadenceProvider"/>/
/// <see cref="IRotationSettingsProvider"/>/<see cref="IStationScopeProvider"/> one seam over:
/// <c>Station:Name</c> and <c>Station:Voice</c> are advertised <c>Live</c> in the settings allowlist,
/// so a boot-frozen identity singleton (the retired <c>StationContext</c>) would be the same class of
/// bug F30/gitea-#211 fixed for scope and cadence.
///
/// <para>
/// Implementations MUST re-evaluate <see cref="Current"/> on every read — never cache the result
/// in a field — so a live identity edit (a renamed station, a repointed default voice) is visible to
/// the very next segment request / <c>/api/stations</c> call / engine push with no process restart.
/// The Host implementation wraps <c>IOptionsMonitor&lt;StationOptions&gt;</c>, which already is the
/// cache; this interface adds nothing beyond a Core-visible accessor.
/// </para>
/// </summary>
public interface IStationIdentityProvider
{
    /// <summary>The station's current identity, evaluated fresh on every call.</summary>
    StationIdentity Current { get; }
}
