namespace GenWave.Orchestration;

/// <summary>
/// The five claim mechanisms a <see cref="Reservation"/> names (SPEC F187.3): the planner marks a
/// slot reserved exactly when it claimed, vended, dequeued, or took the underlying content from a
/// pool. Every other slot (back-announce, lead-in, handoff pieces, time/date, context) carries no
/// <see cref="Reservation"/> at all.
/// </summary>
public enum ReservationKind
{
    /// <summary>An owner announcement claimed by id (SPEC F186's Announcement row).</summary>
    Announcement,

    /// <summary>A pre-rendered ad spot vended by id (SPEC F186's Ad row).</summary>
    AdSpot,

    /// <summary>A crosstalk exchange vended by its clip file, marked at plan time (SPEC F186's Crosstalk row).</summary>
    Crosstalk,

    /// <summary>A speech deferral dequeued from the drain queue, keyed by its own kind (SPEC F186's drained rows).</summary>
    Deferral,

    /// <summary>A station id taken from the authored imaging pool, keyed by media id (SPEC F186's pooled StationId row).</summary>
    StationIdPool,
}
