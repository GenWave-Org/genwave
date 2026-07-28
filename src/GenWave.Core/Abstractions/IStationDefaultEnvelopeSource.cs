using GenWave.Abstractions.Playout;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F91.4 (STORY-241, PLAN T119) — the station-default envelope input
/// <c>GenWave.Orchestration.ScheduleResolver</c> falls back to for a grid gap or a segment's NULL
/// envelope field. Deliberately NOT <see cref="IEnvelopeProvider"/>: PLAN T120 re-implements
/// <see cref="IEnvelopeProvider.Current"/> itself (<c>ScheduleEnvelopeProvider</c>) OVER the resolver,
/// so a resolver that depended on <see cref="IEnvelopeProvider"/> for its own default would create a
/// dependency cycle the moment that wiring lands. This seam exists so the resolver and the future
/// <see cref="IEnvelopeProvider"/> binding can share ONE construction of "the station-default envelope
/// from <c>Station:Envelope:*</c>" — see <c>GenWave.Host.Options.OptionsMonitorEnvelopeProvider</c>'s
/// existing construction, which PLAN T120 wraps in an implementation of this interface instead of
/// duplicating it.
/// </summary>
public interface IStationDefaultEnvelopeSource
{
    /// <summary>
    /// The station-default envelope, evaluated fresh on every call — mirrors
    /// <see cref="IEnvelopeProvider.Current"/>'s live-reload contract, so a live
    /// <c>PUT /api/settings</c> edit to <c>Station:Envelope:*</c> reaches the very next resolve.
    /// </summary>
    SegmentEnvelope Current { get; }
}
