using GenWave.VoiceOff.Score;

namespace GenWave.VoiceOff.Tests.Score;

public static class FeatureResultsPath
{
    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "voiceoff-resultspath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public sealed class ScenarioStartedFromANestedBuildFolder : IDisposable
    {
        // Given a repo with tools/VoiceOff/VoiceOff.csproj
        // When  the default is asked for from tools/VoiceOff/bin/Debug/net10.0
        private readonly string repo = NewFolder();
        private readonly string? path;

        public ScenarioStartedFromANestedBuildFolder()
        {
            string tool = Path.Combine(repo, "tools", "VoiceOff");
            string nested = Path.Combine(tool, "bin", "Debug", "net10.0");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(tool, "VoiceOff.csproj"), "<Project />");
            path = ResultsPath.Default(nested);
        }

        [Fact]
        public void ItIsToolsVoiceOffResultsMdOfThatRepo() =>
            Assert.Equal(Path.Combine(repo, "tools", "VoiceOff", "RESULTS.md"), path);

        public void Dispose() => Directory.Delete(repo, recursive: true);
    }

    public sealed class ScenarioNoToolFolderAbove : IDisposable
    {
        // Given a folder tree with no tools/VoiceOff/VoiceOff.csproj above it
        // When  the default is asked for
        private readonly string folder = NewFolder();
        private readonly string? path;

        public ScenarioNoToolFolderAbove()
        {
            string nested = Path.Combine(folder, "a", "b");
            Directory.CreateDirectory(nested);
            path = ResultsPath.Default(nested);
        }

        [Fact]
        public void ThereIsNoDefault() => Assert.Null(path);

        public void Dispose() => Directory.Delete(folder, recursive: true);
    }
}
