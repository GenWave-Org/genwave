namespace GenWave.Core.Events;

/// <summary>
/// A listener wish was accepted and stored (SPEC F87.1, F87.8; STORY-224, PLAN T87). Deliberately
/// carries NO payload beyond <see cref="StationEvent.OccurredAt"/> — no wish text, no row id, no
/// caller IP — so the wish can never leak through the booth-log narrative pipeline even by
/// accident: F87.7's "never voiced, quoted, or echoed downstream" discipline extends to this
/// event's very shape, not just to how <see cref="Abstractions.IBoothLogEventConsumer"/> happens to
/// summarize it today.
/// </summary>
public sealed record RequestReceived : StationEvent;
