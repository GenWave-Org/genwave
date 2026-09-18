namespace GenWave.Orchestration;

/// <summary>
/// Marks a <see cref="PlannedSlot"/> as claimed, vended, dequeued, or pool-taken content that the
/// planner must never hand out twice (SPEC F187.3). Every slot the planner claimed this way carries
/// exactly one <see cref="Reservation"/>; every other slot carries <see langword="null"/>.
/// </summary>
/// <param name="Kind">Which claim mechanism reserved this slot.</param>
/// <param name="Key">
/// The claimed identity: an announcement/ad-spot id, a crosstalk clip file, a deferral kind name, or
/// a pooled media id — whatever <paramref name="Kind"/> claims by.
/// </param>
public sealed record Reservation(ReservationKind Kind, string Key);
