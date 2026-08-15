namespace GenWave.Orchestration;

/// <summary>
/// SPEC F127.2 (STORY-328, PLAN T285) — the two persona ids a crosstalk exchange casts: the
/// show's own on-air host, and the drop-in neighbor <see cref="CrosstalkPlanner.TryCastPersonas"/>
/// resolved from grid adjacency. Deliberately id-only (never a <c>PersonaCard</c>) — this is the
/// shape a stocked exchange carries forward for its OWN vend-time staleness check (SPEC F127.7:
/// "cast no longer matches the current schedule adjacency"), which only ever needs to compare ids,
/// never re-read either persona's card. <see cref="CrosstalkCastResult"/> is the sibling shape that
/// carries the resolved cards, for a caller (a LATER task's stock-timer loop) that needs them to
/// actually generate/render.
/// </summary>
public sealed record CrosstalkCast(long HostPersonaId, long NeighborPersonaId);
