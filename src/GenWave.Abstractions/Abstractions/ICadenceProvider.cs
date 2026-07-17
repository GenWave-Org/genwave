using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F30.1's precedent applied to cadence (gitea-#211) — the thin accessor seam between
/// <c>GenWave.Orchestration</c> (which references only <c>GenWave.Core</c> and cannot see
/// the Host's <c>IOptionsMonitor&lt;T&gt;</c> / <c>StationOptions</c>) and the Host's live
/// configuration. Mirrors <see cref="IStationScopeProvider"/> one seam over: <c>Station:Cadence:*</c>
/// is advertised <c>Live</c> in the settings allowlist, so a boot-frozen <see cref="CadenceConfig"/>
/// on the retired <c>StationContext</c> singleton would be the same class of bug F30 fixed for
/// scope (see <see cref="IStationIdentityProvider"/>, SPEC F44.1, which retired that singleton
/// entirely).
///
/// <para>
/// Implementations MUST re-evaluate <see cref="Current"/> on every read — never cache the result
/// in a field — so a live cadence edit is visible to the very next planning pass with no process
/// restart. The Host implementation wraps <c>IOptionsMonitor&lt;StationOptions&gt;</c>, which
/// already is the cache; this interface adds nothing beyond a Core-visible accessor.
/// </para>
/// </summary>
public interface ICadenceProvider
{
    /// <summary>The station's current cadence configuration, evaluated fresh on every call.</summary>
    CadenceConfig Current { get; }
}
