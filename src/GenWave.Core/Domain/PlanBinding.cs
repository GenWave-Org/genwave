namespace GenWave.Core.Domain;

/// <summary>
/// The confirm step's own TOCTOU check (SPEC F154.5; STORY-379 AC7; PLAN T379, gh-#529): a minted
/// plan is only honoured if the row's <c>xmin</c> and current path still match what the plan was
/// built against. Pure and static — no I/O, no dependency — so both the planner's own spec and
/// T381's confirm endpoint can lean on the identical rule rather than each re-deriving it.
/// </summary>
public static class PlanBinding
{
    /// <summary>
    /// True when <paramref name="plan"/> is still safe to execute: <paramref name="currentXmin"/>
    /// matches <see cref="FileActionPlan.Xmin"/> (the row was not written in between) AND
    /// <paramref name="currentPath"/> matches <see cref="FileActionPlan.From"/> (the file has not
    /// already moved). Both comparisons are ordinal — an <c>xmin</c> token and a filesystem path are
    /// both machine values, never culture-compared.
    /// </summary>
    public static bool Matches(FileActionPlan plan, string currentXmin, string currentPath) =>
        string.Equals(plan.Xmin, currentXmin, StringComparison.Ordinal)
        && string.Equals(plan.From, currentPath, StringComparison.Ordinal);
}
