namespace GenWave.Orchestration;

/// <summary>
/// The default <see cref="IBreakPlanObserver"/> binding: silence. Mirrors
/// <see cref="NoOpBoundaryFitLog"/>'s own precedent — every construction site that does not care to
/// observe plans (every production host, and every pre-T522 test) keeps compiling and behaving
/// exactly as before.
/// </summary>
public sealed class NoOpBreakPlanObserver : IBreakPlanObserver
{
    /// <summary>Shared instance for non-DI construction (tests).</summary>
    public static readonly NoOpBreakPlanObserver Instance = new();

    /// <inheritdoc/>
    public void Planned(BreakPlan plan)
    {
    }
}
