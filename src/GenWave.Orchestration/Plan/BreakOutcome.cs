using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// <see cref="BreakDelivery.Deliver"/>'s whole result for one break (SPEC F192.1, PLAN T536): the
/// rendered survivors, stamped and ordered exactly as they reach the buffer (<see cref="Items"/>),
/// and what became of every reservation the plan claimed (<see cref="Reservations"/>).
/// </summary>
/// <param name="Items">
/// The rendered survivors, in the SAME ordinal order the plan declared its slots (SPEC F192.1) — a
/// dropped or abandoned slot contributes nothing here.
/// </param>
/// <param name="Reservations">
/// One <see cref="ReservationOutcome"/> per <see cref="Reservation"/> the plan's slots carried (SPEC
/// F192.4) — <see cref="Reservation"/>'s own value equality makes it a safe dictionary key, since the
/// planner never claims the same key twice within one plan.
/// </param>
public sealed record BreakOutcome(
    IReadOnlyList<MediaItem> Items,
    IReadOnlyDictionary<Reservation, ReservationOutcome> Reservations);
