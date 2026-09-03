namespace GenWave.Host.Tests.Support;

/// <summary>
/// Walks up from a starting directory until <c>GenWave.sln</c> turns up — the one repo-root walk-up
/// this assembly needs, in one place (T394 review fold: <c>ExamplePluginBuildOutput</c> and
/// <c>Story386_PluginDoorVisibleAndAdditive</c>'s own SEAMS byte-diff fact each carried their own
/// copy). Deliberately NOT shared with <c>GenWave.Plugins.Tests.Support.ExamplePluginPayload</c>'s own
/// identical-looking walk-up — that class's own remarks explain why a CROSS-assembly dependency here
/// would cost more than the few duplicated lines: pulling in another test project's own project graph
/// for a five-line loop. This type only closes the duplication WITHIN this one assembly.
/// </summary>
internal static class RepoRootLocator
{
    public static string Find(string startDirectory)
    {
        for (var current = new DirectoryInfo(startDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "GenWave.sln")))
                return current.FullName;
        }

        throw new InvalidOperationException($"Could not find GenWave.sln walking up from \"{startDirectory}\".");
    }
}
