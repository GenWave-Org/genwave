namespace GenWave.Core.Events;

/// <summary>
/// A periodic listener-count sample (gh-#10, plugin-readiness P1.4): the station had
/// <see cref="Listeners"/> concurrent listeners at <see cref="StationEvent.OccurredAt"/>, as
/// reported by Icecast's admin stats. Published by the Host's listener-stats poller on a fixed
/// cadence — the time-series feed a future analytics module subscribes to through the
/// <see cref="Abstractions.IStationEventSink"/> spine, exactly like every other station event.
///
/// Only REAL samples are ever published: a poll where the count cannot be determined (Icecast
/// down/unreachable, stats disabled) publishes nothing rather than a null/zero guess — an absent
/// sample is honest; a fabricated zero would poison the series.
/// </summary>
public sealed record ListenerCountSampled(int Listeners) : StationEvent;
