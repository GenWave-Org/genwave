namespace GenWave.Orchestration;

/// <summary>
/// The slot was never attempted at all — e.g. a vend that threw before any render was reached
/// (SPEC F186's Ad row: "vend throws → logged, no slot").
/// </summary>
public sealed record AbandonedOutcome : SlotOutcome;
