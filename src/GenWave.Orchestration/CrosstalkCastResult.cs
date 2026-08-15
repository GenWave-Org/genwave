namespace GenWave.Orchestration;

using GenWave.Core.Domain;

/// <summary>
/// SPEC F127.2 (STORY-328, PLAN T285) — <see cref="CrosstalkPlanner.TryCastAsync"/>'s full answer:
/// the cast persona-id pair (<see cref="Cast"/>, the shape a stocked exchange keeps for its own
/// vend-time staleness check) plus the two resolved <see cref="PersonaCard"/>s a LATER task's
/// stock-timer loop needs to build a <c>GenWave.Tts.CrosstalkExchangeRequest</c> — mirrors that
/// type's own "already CAST by the caller" contract: this is the caller producing exactly that.
/// </summary>
public sealed record CrosstalkCastResult(CrosstalkCast Cast, PersonaCard HostCard, PersonaCard NeighborCard);
