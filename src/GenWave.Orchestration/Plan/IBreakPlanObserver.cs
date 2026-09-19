namespace GenWave.Orchestration;

/// <summary>
/// A seam that watches every <see cref="BreakPlan"/> the moment <see cref="BreakPlanner.PlanAsync"/>
/// hands it back to the Orchestrator (PLAN T522) — no production behavior depends on it, so a host
/// that never registers one degrades to <see cref="NoOpBreakPlanObserver"/>. A test double
/// (<c>CapturingBreakPlanObserver</c>) is the only reason this exists today: plans are otherwise
/// consumed and discarded inline by the render phase, with nothing else ever needing to see one.
/// </summary>
public interface IBreakPlanObserver
{
    /// <summary>Called once per unit, immediately after the plan phase builds <paramref name="plan"/>.</summary>
    void Planned(BreakPlan plan);
}
