using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F30.1 — the thin scope-accessor seam between <c>GenWave.Orchestration</c> (which
/// references only <c>GenWave.Core</c> and cannot see the Host's <c>IOptionsMonitor&lt;T&gt;</c>
/// / <c>StationOptions</c>) and the Host's live configuration. Both sides of that boundary depend
/// on this one interface instead of inventing separate idioms.
///
/// <para>
/// Implementations MUST re-evaluate <see cref="Current"/> on every read — never cache the result
/// in a field — so a live scope edit (F30) is visible to the very next selection/browse call with
/// no process restart. The Host implementation wraps <c>IOptionsMonitor&lt;StationOptions&gt;</c>,
/// which already is the cache; this interface adds nothing beyond a Core-visible accessor.
/// </para>
/// </summary>
public interface IStationScopeProvider
{
    /// <summary>The station's current default library scope, evaluated fresh on every call.</summary>
    LibraryScope Current { get; }
}
