namespace GenWave.SeamIndexGenerator;

/// <summary>Finds the repo root by walking up from <see cref="AppContext.BaseDirectory"/> — the
/// PROCESS's own base directory (the entry assembly's output folder), not whichever assembly happens
/// to contain the currently executing code — until <c>GenWave.sln</c> turns up. Robust to build
/// configuration (Debug/Release) and to wherever the process is invoked from, without hardcoding a
/// path depth or a machine-specific path. Public
/// (not the usual <c>internal</c> for this project's helpers) specifically so
/// <c>GenWave.Architecture.Tests</c>'s own <c>SolutionLocator</c> — which already references this
/// project — can delegate here instead of carrying a second copy of the same walk-up loop.</summary>
public static class RepoRoot
{
    public static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "GenWave.sln");
            if (File.Exists(candidate))
                return dir.FullName;
        }

        throw new FileNotFoundException($"GenWave.sln not found above {AppContext.BaseDirectory}.");
    }
}
