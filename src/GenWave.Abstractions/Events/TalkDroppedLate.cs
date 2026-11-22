namespace GenWave.Core.Events;

/// <summary>
/// A talk segment's render ran past the engine's budget, so it was dropped and the music carries on
/// (SPEC F219.26, PLAN T767). A render that failed outright is not this: that is the voice tile's red,
/// not lateness. <paramref name="Kind"/> is the <see cref="GenWave.Core.Domain.SegmentKind"/> name kept as a plain
/// string (the <see cref="HandoffPieceDropped"/> precedent); <paramref name="PersonaName"/> is the DJ the
/// request was for, when there is one.
/// </summary>
public sealed record TalkDroppedLate(string Kind, string? PersonaName = null) : StationEvent;
