namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F158.3 (STORY-388, PLAN T397) — the thin accessor seam between
/// <c>GenWave.Orchestration</c> (which references only <c>GenWave.Core</c>/<c>GenWave.Abstractions</c>
/// and cannot see the Host's <c>IOptionsMonitor&lt;T&gt;</c>/<c>StationOptions</c> directly) and the
/// Host's live <c>Station:Ads:EveryNUnits</c> configuration. Mirrors <see cref="IBoundaryBiasProvider"/>
/// one seam over: a single scalar knob, read fresh on every call rather than cached.
///
/// <para>
/// Implementations MUST re-evaluate <see cref="Current"/> fresh on every call — never cache the
/// result in a field (the same discipline every sibling provider in this folder follows) — so a live
/// <c>Station:Ads:EveryNUnits</c> edit governs the very next unit's cadence check with no process
/// restart. The Host's <c>IOptionsMonitor</c>-backed implementation
/// (<c>OptionsMonitorAdCadenceProvider</c>) is that binding, registered by
/// <c>StationOptionsServiceCollectionExtensions.AddGenWaveStationOptions</c>.
/// <see cref="NoOpAdCadenceProvider"/> remains <c>GenWave.Orchestration</c>'s own fail-closed default
/// for any composition that never wires the Host binding (e.g. a test) — keeping
/// <c>Orchestrator</c>, and every test built against it, compiling and inert rather than failing to
/// compose.
/// </para>
/// </summary>
public interface IAdCadenceProvider
{
    /// <summary>
    /// <c>Station:Ads:EveryNUnits</c>'s current value, evaluated fresh on every call — the
    /// <see cref="ICadenceProvider.Current"/>-&gt;<c>StationIdEveryNUnits</c> twin. Zero (the
    /// default) disables the ad cadence trigger entirely; a positive value fires the trigger on
    /// every unit whose count is an exact multiple of it (unit 0 never fires).
    /// </summary>
    int Current { get; }
}
