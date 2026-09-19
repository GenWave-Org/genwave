using GenWave.Orchestration;

namespace GenWave.TestSupport.Fakes;

/// <summary>
/// Recording <see cref="IBreakPlanObserver"/> double (PLAN T522) — collects every
/// <see cref="BreakPlan"/> the plan phase hands back, in build order, so a spec can assert on each
/// unit's own plan (e.g. its <see cref="BreakPlan.ToTrace"/> line) after a run. Mirrors
/// <see cref="CapturingStationEventSink"/>'s own record-and-expose shape. Test-scope only.
/// </summary>
public sealed class CapturingBreakPlanObserver : IBreakPlanObserver
{
    /// <summary>Every plan the plan phase built, in build order — one per unit.</summary>
    public List<BreakPlan> Plans { get; } = [];

    /// <summary>Records <paramref name="plan"/> into <see cref="Plans"/>.</summary>
    public void Planned(BreakPlan plan) => Plans.Add(plan);
}
