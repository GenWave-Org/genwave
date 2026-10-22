// STORY-501 — Nothing ships, and no slug in code (gh-#614 · SPEC F214.16, F214.17 · PLAN T606)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.
// AC1, AC2, AC6 (the laws) live in GenWave.Architecture.Tests' Story501_VoiceOffSlugFence.

using System.Xml.Linq;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureNothingShipsAndNoSlugInCode
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC3 — the tool references only Tts and Loudness
    public sealed class ScenarioTheToolReferencesOnlyTtsAndLoudness
    {
        // Given tools/VoiceOff/VoiceOff.csproj
        // When  its ProjectReferences are listed
        private readonly string[] references;

        public ScenarioTheToolReferencesOnlyTtsAndLoudness()
        {
            references = XDocument.Load(FindToolProject())
                .Descendants("ProjectReference")
                .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        [Fact]
        public void TheyAreGenWaveTtsAndGenWaveLoudness() =>
            Assert.Equal(["GenWave.Loudness", "GenWave.Tts"], references);

        private static string FindToolProject() =>
            Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "tools", "VoiceOff", "VoiceOff.csproj");
    }

    // AC4 — the solution builds it
    public sealed class ScenarioTheSolutionBuildsIt
    {
        // Given GenWave.sln
        // When  `dotnet build GenWave.sln` runs
        private readonly string builtAssembly = typeof(Cli).Assembly.GetName().Name ?? string.Empty;

        [Fact]
        public void VoiceOffDllIsProduced() => Assert.Equal("VoiceOff", builtAssembly);
    }

    // AC5 — help lists every verb
    public sealed class ScenarioHelpListsEveryVerb
    {
        // Given the built tool
        // When  `voiceoff --help` runs
        private readonly string help;

        public ScenarioHelpListsEveryVerb()
        {
            StringWriter output = new();
            Cli.Run(["--help"], output, TextWriter.Null);
            help = output.ToString();
        }

        [Theory]
        [InlineData("voiceoff render --tier cpu")]
        [InlineData("voiceoff pair --run <run-id>")]
        [InlineData("voiceoff serve --run <run-id> --port 8765")]
        [InlineData("voiceoff reel --run <run-id>")]
        [InlineData("voiceoff score --run <run-id>")]
        public void EachVerbPrintsWithAnExample(string example) =>
            Assert.Contains($"Example: {example}", help, StringComparison.Ordinal);
    }
}
