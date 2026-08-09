namespace GenWave.Orchestration;

/// <summary>
/// The default <see cref="IBoundaryFitLog"/> binding: silence. Mirrors
/// <see cref="NoOpPersonaPickProvider"/>/<see cref="NoOpRequestFulfillmentSource"/>'s own precedent
/// one file up — every construction site that does not care to observe boundary-fit outcomes (a
/// future non-Orchestrator caller, or a test that never scripts one) keeps compiling and behaving
/// exactly as before.
/// </summary>
sealed class NoOpBoundaryFitLog : IBoundaryFitLog
{
    /// <summary>Shared instance for non-DI construction.</summary>
    public static readonly NoOpBoundaryFitLog Instance = new();

    /// <inheritdoc/>
    public void Log(
        BoundaryFitPlan fit, string outcome, BoundaryOutcome rung, IReadOnlyList<TimeSpan> sampled,
        TimeSpan? chosenDiff)
    {
    }
}
