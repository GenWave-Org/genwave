namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Walks up from a starting directory until <c>GenWave.sln</c> turns up; throws when it never does.</summary>
internal static class RepoRootLocator
{
    public static string Find(string startDirectory)
    {
        for (DirectoryInfo? current = new(startDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "GenWave.sln")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException($"Could not find GenWave.sln walking up from \"{startDirectory}\".");
    }
}
