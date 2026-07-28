namespace GenWave.Core.Events;

/// <summary>
/// A handoff ceremony piece (sign-off or sign-on) failed to render at a roster boundary — the F92.4
/// ruled degrade ladder's "drop" outcome (STORY-243, PLAN T124). <paramref name="Kind"/> names which
/// piece (<c>"SignOff"</c>/<c>"SignOn"</c>, <see cref="GenWave.Core.Domain.SegmentKind"/>'s own name
/// kept as a plain string here for the same dependency-free reason <see cref="DegradationModeChanged"/>
/// keeps its own mode names plain); <paramref name="Cause"/> is a short human-readable reason (render
/// budget exceeded, a render fault, or T123's own null-for-non-fresh-copy drop).
///
/// <para>
/// The OTHER piece of the same boundary, if it rendered, still airs (F92.4 — "whichever piece
/// rendered"); the next boundary attempts the full ceremony again on its own next enqueue — nothing
/// here latches a failure or blocks a future retry.
/// </para>
/// </summary>
public sealed record HandoffPieceDropped(string Kind, string Cause) : StationEvent;
