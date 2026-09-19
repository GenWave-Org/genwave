namespace GenWave.Orchestration;

/// <summary>The slot's render returned null or threw, and was dropped (SPEC F186.3).</summary>
public sealed record FailedOutcome : SlotOutcome;
