// STORY-520 — the hour-mix instrument has its own name (F216 · PLAN T649)
//
// BDD specification — xUnit. Existence/absence facts over tools/ plus a reader sweep over tests/
// and tools/. Story304 (MediaLibrary.Tests, Integration) executing the renamed file is that
// spec's own job. Every fact is pending until T649.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureHourMixGateHasItsOwnName
{
    static string Root => RepoRootLocator.Find(AppContext.BaseDirectory);

    // Assembled at runtime so this file never names what it forbids.
    static readonly string OldName = "demo_" + "hour_gate";

    public static class ScenarioTheFileIsRenamed
    {
        [Fact(Skip = "pending: T649")]
        public static void Hour_mix_gate_exists()
        {
            Assert.True(File.Exists(Path.Combine(Root, "tools", "hour_mix_gate.sql")));
        }

        [Fact(Skip = "pending: T649")]
        public static void The_old_file_does_not_exist()
        {
            Assert.False(File.Exists(Path.Combine(Root, "tools", OldName + ".sql")));
        }
    }

    public static class ScenarioEveryReaderFollows
    {
        static IEnumerable<string> Readers() =>
            new[] { "tests", "tools" }
                .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(Root, dir), "*", SearchOption.AllDirectories))
                .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".sh", StringComparison.Ordinal) || f.EndsWith(".sql", StringComparison.Ordinal))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        [Fact(Skip = "pending: T649")]
        public static void No_reader_names_the_old_file()
        {
            Assert.Empty(Readers().Where(f => File.ReadAllText(f).Contains(OldName, StringComparison.Ordinal)).Select(f => Path.GetFileName(f)));
        }

        [Fact(Skip = "pending: T649")]
        public static void Story304_executes_the_new_file()
        {
            var story304 = File.ReadAllText(Path.Combine(Root, "tests", "GenWave.MediaLibrary.Tests", "Specs", "Story304_AiredKindStamp.cs"));

            Assert.Contains("hour_mix_gate.sql", story304, StringComparison.Ordinal);
        }
    }
}
