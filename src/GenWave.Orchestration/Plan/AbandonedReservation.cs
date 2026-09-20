namespace GenWave.Orchestration;

/// <summary>
/// The reservation's unit was cancelled before delivery even started (SPEC F192.5) — no slot of the
/// unit reached the buffer, and every reservation the plan claimed is abandoned, not merely dropped.
/// </summary>
public sealed record AbandonedReservation : ReservationOutcome;
