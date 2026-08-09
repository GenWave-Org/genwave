namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// SPEC F110.1/F110.3 (STORY-301/302, PLAN T230) — the thin accessor seam between
/// <c>GenWave.Orchestration</c>'s <c>ClockAnchoredImagingProducer</c> (which references only
/// <c>GenWave.Core</c>/<c>GenWave.Abstractions</c> and cannot see the Host's
/// <c>IOptionsMonitor&lt;T&gt;</c> directly) and the Host's live <c>Station:Imaging:*</c>
/// configuration. Mirrors <see cref="IStationLocationProvider"/>/<see cref="IContextSettingsProvider"/>
/// one seam over.
///
/// <para>
/// Implementations MUST re-evaluate <see cref="Current"/> fresh on every call — never cache the
/// result in a field (the same discipline every sibling provider in this folder follows) — so a live
/// <c>Station:Imaging:*</c> edit governs the very next producer tick with no process restart. The
/// Host's <c>IOptionsMonitor</c>-backed implementation (<c>OptionsMonitorStationImagingProvider</c>)
/// is that binding — this IS PLAN T230, wired into the Host's composition root.
/// <see cref="NoOpStationImagingSettingsProvider"/> remains <c>GenWave.Orchestration</c>'s own
/// fail-closed default for any composition that never wires the Host binding (e.g. a test, or a
/// future non-Host consumer) — keeping the producer, and every test built against it, compiling and
/// inert rather than failing to compose.
/// </para>
/// </summary>
public interface IStationImagingSettingsProvider
{
    /// <summary>The station's currently configured clock-anchored imaging settings, evaluated fresh
    /// on every call.</summary>
    StationImagingSettings Current { get; }
}
