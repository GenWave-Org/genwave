namespace GenWave.Orchestration;

/// <summary>
/// What became of a <see cref="PlannedSlot"/> once delivery was attempted — a closed hierarchy of
/// exactly four cases, each declared in its own sibling file: <see cref="RenderedOutcome"/>,
/// <see cref="TimedOutOutcome"/>, <see cref="FailedOutcome"/>, <see cref="AbandonedOutcome"/>. Not
/// produced by the plan phase itself (SPEC F187.6) — a later render-phase type, declared here
/// because PLAN T520 owns the whole plan vocabulary in one pass.
/// </summary>
public abstract record SlotOutcome
{
    private protected SlotOutcome() { }
}
