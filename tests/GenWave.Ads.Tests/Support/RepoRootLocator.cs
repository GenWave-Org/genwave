namespace GenWave.Ads.Tests.Support;

/// <summary>
/// Walks up from a starting directory until <c>GenWave.sln</c> turns up — this assembly's own copy of
/// the walk-up (see <c>GenWave.Host.Tests.Support.RepoRootLocator</c>'s own remarks for why a
/// cross-assembly dependency is not worth it for a five-line loop). Used by the STORY-390 AC6 scope
/// pin: no non-ad production source may reference <see cref="AdProfanityList"/>.
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
