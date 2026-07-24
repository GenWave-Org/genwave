namespace GenWave.Core.Events;

/// <summary>
/// A pending listener request aired (SPEC F87.6, F87.7, F87.8; STORY-227, PLAN T90): <c>pending →
/// fulfilled</c>, published the instant the fulfillment rung SELECTS the winning candidate — see
/// <c>GenWave.Orchestration.RequestFulfillmentProvider</c>'s own remarks for why the one-shot stamp
/// lands there rather than at the feeder's later push. Deliberately carries NO payload beyond
/// <see cref="StationEvent.OccurredAt"/> — no wish text, no media id, no artist/title — the same
/// discipline <see cref="RequestReceived"/>/<see cref="RequestEvicted"/>/<see cref="RequestExpired"/>
/// already establish; the fulfilled track's own identity already reaches the booth log through the
/// ordinary <see cref="TrackAired"/> row (SPEC F72.1), so this event exists purely to narrate the
/// REQUEST's own outcome, not the track's.
/// </summary>
public sealed record RequestFulfilled : StationEvent;
