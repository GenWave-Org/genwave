namespace GenWave.Architecture.Tests.Support;

/// <summary>Finds <c>GenWave.sln</c> by walking up from the test binary's own output directory —
/// robust to build configuration (Debug/Release) without hardcoding a path depth.</summary>
internal static class SolutionLocator
{
    public static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "GenWave.sln");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"GenWave.sln not found above {AppContext.BaseDirectory}.");
    }

    /// <summary>The repo root — <see cref="Find"/>'s containing directory. Three call sites
    /// (<c>DepsJsonDependencyScan</c>, <c>Story291_ConventionLaws</c>'s <c>Program.cs</c> reader,
    /// <c>ContributingDocument</c>) each used to repeat "<c>Path.GetDirectoryName(Find()) ?? throw
    /// ...</c>" independently (STORY-293 review) — lifted here once so the null-check for "a solution
    /// file with no containing directory" (a condition that can't actually happen, since
    /// <see cref="Find"/> only ever returns a path under a real directory it just walked) has exactly
    /// one place to live.</summary>
    public static string Root() =>
        Path.GetDirectoryName(Find())
            ?? throw new InvalidOperationException($"\"{Find()}\" has no containing directory.");
}
