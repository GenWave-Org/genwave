// STORY-529 — The blind harness is gone and nothing ships (gh-#614 · SPEC F217 · PLAN T660)
//
// BDD specification — xUnit. LIVE at T660, except GauntletDoesNotExist (pending T663).
// AC4 and AC7 (the slug fence over scratch trees) live in GenWave.Architecture.Tests'
// Story529_SlugFenceDataFolders, because the fence is internal to that assembly.

using System.Xml.Linq;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheBlindHarnessIsGone
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — help lists three verbs
    public sealed class ScenarioHelpListsThreeVerbs
    {
        // Given the built tool
        // When  voiceoff --help runs
        private readonly string[] verbs;

        public ScenarioHelpListsThreeVerbs()
        {
            StringWriter output = new();
            Cli.Run(["--help"], output, TextWriter.Null);
            verbs = output.ToString()
                .Split('\n')
                .SkipWhile(line => !line.StartsWith("Verbs:", StringComparison.Ordinal))
                .Skip(1)
                .Where(line => line.StartsWith("  ", StringComparison.Ordinal) && !line.StartsWith("   ", StringComparison.Ordinal)
                    && line.Trim().Length > 0)
                .Select(line => line.Trim().Split(' ')[0])
                .ToArray();
        }

        [Fact]
        public void ItListsRenderServeAndScore() =>
            Assert.Equal(["render", "score", "serve"], verbs.Order(StringComparer.Ordinal).ToArray());

        [Fact]
        public void ItListsNothingElse() => Assert.Equal(3, verbs.Length);
    }

    // AC2 — the old folders are gone
    public sealed class ScenarioTheOldFoldersAreGone
    {
        // Given tools/VoiceOff
        // When  its folders are listed

        [Fact]
        public void PairingReelingAndTimingDoNotExist()
        {
            string tool = Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "tools", "VoiceOff");
            string[] present = new[] { "Pairing", "Reeling", "Timing" }.Where(name => Directory.Exists(Path.Combine(tool, name))).ToArray();
            Assert.Empty(present);
        }

        [Fact(Skip = "pending: T663 — see docs/PLAN.md")]
        public void GauntletDoesNotExist() => Assert.Fail("pending: T663 — gauntlet/ does not exist");
    }

    // AC3 — the old specs are gone
    public sealed class ScenarioTheOldSpecsAreGone
    {
        // Given tests/GenWave.VoiceOff.Tests/Specs
        // When  its files are listed

        [Fact]
        public void Story496ToStory500DoNotExist()
        {
            string specs = Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "tests", "GenWave.VoiceOff.Tests", "Specs");
            string[] present = Directory.EnumerateFiles(specs, "Story*.cs")
                .Select(path => Path.GetFileName(path))
                .Where(name => new[] { "Story496_", "Story497_", "Story498_", "Story499_", "Story500_" }
                    .Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                .ToArray();
            Assert.Empty(present);
        }
    }

    // AC5 — the tool still references only Tts and Loudness
    public sealed class ScenarioTheToolStillReferencesOnlyTtsAndLoudness
    {
        // Given VoiceOff.csproj
        // When  its ProjectReferences are listed
        private readonly string[] references = XDocument
            .Load(Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "tools", "VoiceOff", "VoiceOff.csproj"))
            .Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();

        [Fact]
        public void TheyAreGenWaveLoudnessAndGenWaveTts() =>
            Assert.Equal(["GenWave.Loudness", "GenWave.Tts"], references);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC6 — a retired verb is unknown
    public sealed class ScenarioARetiredVerbIsUnknown
    {
        // Given the built tool
        // When  voiceoff pair runs

        [Fact]
        public void ItExits2() => Assert.Equal(2, Cli.Run(["pair"], TextWriter.Null, TextWriter.Null));
    }
}
