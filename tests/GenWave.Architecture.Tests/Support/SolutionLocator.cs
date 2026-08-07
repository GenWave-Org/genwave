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
}
