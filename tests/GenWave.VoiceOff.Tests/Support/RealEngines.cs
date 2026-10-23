namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Where the tool's shipped <c>engines</c> directory sits in the repo.</summary>
internal static class RealEngines
{
    internal static string Directory =>
        Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "tools", "VoiceOff", "engines");
}
