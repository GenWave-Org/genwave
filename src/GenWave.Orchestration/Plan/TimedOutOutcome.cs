namespace GenWave.Orchestration;

/// <summary>The slot's render exceeded the plan's render budget and was dropped (SPEC F186.3).</summary>
public sealed record TimedOutOutcome : SlotOutcome;
