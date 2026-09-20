namespace GenWave.Orchestration;

/// <summary>
/// What became of a <see cref="Reservation"/> once delivery was attempted (SPEC F192.4) — a closed
/// hierarchy of exactly three cases, each declared in its own sibling file: <see cref="AiredReservation"/>,
/// <see cref="DroppedReservation"/>, <see cref="AbandonedReservation"/>. Nothing is released on any
/// outcome (today's behaviour, unchanged) — this is the record a later story can act on, not yet an
/// action itself.
/// </summary>
public abstract record ReservationOutcome
{
    private protected ReservationOutcome() { }
}
