namespace GenWave.Architecture.Tests.Support;

/// <summary>Finds <c>GenWave.sln</c> and the repo root. Delegates the actual walk-up to
/// <c>GenWave.SeamIndexGenerator.RepoRoot</c> (T216, STORY-294) rather than carrying a second copy
/// of the same loop — this project already references that tool for <c>SeamIndexDocument</c>.</summary>
internal static class SolutionLocator
{
    public static string Find() => Path.Combine(Root(), "GenWave.sln");

    /// <summary>The repo root — <see cref="Find"/>'s containing directory. Three call sites
    /// (<c>DepsJsonDependencyScan</c>, <c>Story291_ConventionLaws</c>'s <c>Program.cs</c> reader,
    /// <c>ContributingDocument</c>) each used to repeat "<c>Path.GetDirectoryName(Find()) ?? throw
    /// ...</c>" independently (STORY-293 review) — lifted here once. T216 (STORY-294) then moved the
    /// walk-up itself into <c>GenWave.SeamIndexGenerator.RepoRoot.Find</c>, which returns the root
    /// directory directly (no <c>GetDirectoryName</c>/null-check needed at all anymore); this method
    /// is now a one-line delegation kept for its three existing call sites' sake.</summary>
    public static string Root() => GenWave.SeamIndexGenerator.RepoRoot.Find();
}
