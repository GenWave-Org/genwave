namespace GenWave.Orchestration;

/// <summary>
/// The slot's render returned null or threw, and was dropped (SPEC F186.3). <see cref="Exception"/>
/// carries the fault when the render threw (SPEC F191.3); a render that merely completed with null
/// leaves it <see langword="null"/> rather than fabricating one.
/// </summary>
public sealed record FailedOutcome(Exception? Exception = null) : SlotOutcome;
