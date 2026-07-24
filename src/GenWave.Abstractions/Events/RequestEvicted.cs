namespace GenWave.Core.Events;

/// <summary>
/// The station-wide pending cap forced the oldest pending wish out to make room for a new one
/// (SPEC F87.3, F87.8; STORY-224, PLAN T87). <c>station.request</c>'s own three outcomes
/// (received/fulfilled/expired, F87.8) have no "evicted" member — this is a narrative-only event
/// for the booth log, not a fourth request outcome, and carries NO payload beyond
/// <see cref="StationEvent.OccurredAt"/>: no wish text (evicted or new), no row id, so nothing about
/// either wish is ever inferable from this event.
/// </summary>
public sealed record RequestEvicted : StationEvent;
