namespace GenWave.Orchestration;

/// <summary>The reservation's slot failed to render and was dropped (SPEC F192.4).</summary>
/// <param name="Reason">The drop's cause, the same text <see cref="BreakDelivery"/>'s own WARN (when one fires) names.</param>
public sealed record DroppedReservation(string Reason) : ReservationOutcome;
