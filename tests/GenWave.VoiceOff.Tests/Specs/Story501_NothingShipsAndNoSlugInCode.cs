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
        [InlineData("voiceoff serve --run <run-id> --port 8765")]
        [InlineData("voiceoff score --run <run-id>")]
        public void EachVerbPrintsWithAnExample(string example) =>
            Assert.Contains($"Example: {example}", help, StringComparison.Ordinal);
    }

    public sealed class ScenarioAVerbAnswersHelp
    {
        // Given a verb
        // When  `voiceoff <verb> --help` runs
        private readonly int exit;
        private readonly string help;
        private readonly string renderHelp;

        public ScenarioAVerbAnswersHelp()
        {
            StringWriter output = new();
            exit = Cli.Run(["serve", "--help"], output, TextWriter.Null);
            help = output.ToString();
            StringWriter render = new();
            Cli.Run(["render", "-h"], render, TextWriter.Null);
            renderHelp = render.ToString();
        }

        [Fact]
        public void ItExitsZero() => Assert.Equal(0, exit);

        [Fact]
        public void ItPrintsThatVerbsExample() =>
            Assert.Contains("voiceoff serve --run <run-id>", help, StringComparison.Ordinal);

        [Fact]
        public void TheShortFlagWorksToo() =>
            Assert.Contains("voiceoff render --tier cpu", renderHelp, StringComparison.Ordinal);

        [Fact]
        public void TheRenderExampleListsTheRootFolder() =>
            Assert.Contains("[--root <dir>]", renderHelp, StringComparison.Ordinal);
    }
}
