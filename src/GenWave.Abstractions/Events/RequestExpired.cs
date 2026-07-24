namespace GenWave.Core.Events;

/// <summary>
/// A pending listener request passed its fulfillment window unfulfilled (SPEC F87.6, F87.8; STORY-227,
/// PLAN T90): <c>pending → expired</c>. Deliberately carries NO payload beyond
/// <see cref="StationEvent.OccurredAt"/> — no wish text, no row id — the same discipline
/// <see cref="RequestReceived"/>/<see cref="RequestEvicted"/> already establish. Published once per
/// row the opportunistic sweep (<c>GenWave.Orchestration.RequestFulfillmentProvider</c>) actually
/// expires on a given pick — a backlog of N stale rows narrates as N booth-log lines, one per row,
/// never one summary event.
/// </summary>
public sealed record RequestExpired : StationEvent;
