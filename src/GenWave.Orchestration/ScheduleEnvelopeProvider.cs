using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;

namespace GenWave.Orchestration;

/// <summary>
/// The resolver-backed <see cref="IEnvelopeProvider"/> (SPEC F91.7, STORY-241, PLAN T120): replaces
/// the single 24/7 station-default binding with the schedule grid's own per-segment answer.
/// <see cref="Current"/>/<see cref="EnvelopeId"/> both read
/// <see cref="CachingScheduleResolver.TryGetCurrent"/> synchronously — no store round trip, no
/// awaiting — so <c>Orchestrator</c>'s per-pick candidate query and its per-pick debug log line both
/// observe the SAME on-air segment's envelope with zero call-site change (F91.5's "re-backed, not
/// re-plumbed" shape).
///
/// <para>
/// Boot-window semantics (mirrors <see cref="OnAirPersonaAccessor.ActivePersonaId"/>'s own remarks):
/// before <paramref name="scheduleResolver"/> has completed its first
/// <see cref="CachingScheduleResolver.ResolveAsync"/>, <see cref="CachingScheduleResolver.TryGetCurrent"/>
/// answers <see langword="null"/> — this provider then falls back to
/// <paramref name="stationDefault"/>'s own value / <see cref="IEnvelopeProvider.StationDefaultSentinel"/>,
/// the exact F91.4 gap contract, so the very first pick behaves identically to a genuine grid gap
/// rather than stalling or throwing.
/// </para>
/// </summary>
public sealed class ScheduleEnvelopeProvider(
    CachingScheduleResolver scheduleResolver, IStationDefaultEnvelopeSource stationDefault) : IEnvelopeProvider
{
    /// <inheritdoc/>
    public SegmentEnvelope Current => scheduleResolver.TryGetCurrent()?.Envelope ?? stationDefault.Current;

    /// <inheritdoc/>
    public string EnvelopeId => scheduleResolver.TryGetCurrent()?.Segment?.Id is { } segmentId
        ? $"segment:{segmentId}"
        : IEnvelopeProvider.StationDefaultSentinel;
}
