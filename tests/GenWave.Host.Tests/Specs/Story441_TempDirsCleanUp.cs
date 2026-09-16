// STORY-441 — Test temp directories clean up after themselves (gh-#710 · SPEC F181.2 · PLAN T479/T480)
//
// BDD specification — xUnit. AC1–AC3 drive TempDir; AC5–AC7 drive TempSweep.Run over a scratch root
// (never the real temp root); AC4 and AC8 are source pins over tests/GenWave.Host.Tests.
//
// Was red at plan time (throwing skeletons, 28 files creating their own scratch directories);
// green since T479/T480.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTestTempDirectoriesCleanUpAfterThemselves
{
    // Split so this file's own source text never trips the scan below (which greps for the very
    // same API name it is asserting nothing but TempDir.cs still calls).
    const string Needle = "CreateTemp" + "Subdirectory";

    static string HostTestsDir =>
        Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "tests", "GenWave.Host.Tests");

    // ---------------------------------------------------------------------
    // HAPPY PATH — TempDir
    // ---------------------------------------------------------------------

    public sealed class ScenarioANewTempDirLivesUnderTheTempRoot
    {
        readonly string path;

        public ScenarioANewTempDirLivesUnderTheTempRoot()
        {
            using var dir = new TempDir();
            path = dir.Path;
        }

        [Fact]
        public void StartsWithTheTempRootAndPrefix() =>
            Assert.StartsWith(Path.Combine(Path.GetTempPath(), TempDir.Prefix), path, StringComparison.Ordinal);
    }

    public sealed class ScenarioDisposeDeletesRecursively
    {
        readonly string path;

        public ScenarioDisposeDeletesRecursively()
        {
            var dir = new TempDir();
            path = dir.Path;
            Directory.CreateDirectory(Path.Combine(path, "nested"));
            File.WriteAllText(Path.Combine(path, "nested", "file.txt"), "x");
            dir.Dispose();
        }

        [Fact]
        public void TheDirectoryIsGone() => Assert.False(Directory.Exists(path));
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — TempSweep
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheSweepRemovesStaleAndKeepsFresh : IDisposable
    {
        readonly TempDir root = new();

        public ScenarioTheSweepRemovesStaleAndKeepsFresh()
        {
            var stale = DateTime.UtcNow - TimeSpan.FromHours(2);
            foreach (var name in new[] { "gw-stale", "genwave-pawire-x", "story343-env-x", "gh332-x", "unrelated-x" })
            {
                var dir = Directory.CreateDirectory(Path.Combine(root.Path, name));
                Directory.SetLastWriteTimeUtc(dir.FullName, stale);
            }
            Directory.CreateDirectory(Path.Combine(root.Path, "gw-fresh"));

            TempSweep.Run(root.Path, olderThan: TimeSpan.FromHours(1));
        }

        public void Dispose() => root.Dispose();

        [Fact]
        public void GwStaleIsGone() => Assert.False(Directory.Exists(Path.Combine(root.Path, "gw-stale")));

        [Fact]
        public void GwFreshRemains() => Assert.True(Directory.Exists(Path.Combine(root.Path, "gw-fresh")));

        [Fact]
        public void PawireIsGone() => Assert.False(Directory.Exists(Path.Combine(root.Path, "genwave-pawire-x")));

        [Fact]
        public void Story343EnvIsGone() => Assert.False(Directory.Exists(Path.Combine(root.Path, "story343-env-x")));

        [Fact]
        public void Gh332IsGone() => Assert.False(Directory.Exists(Path.Combine(root.Path, "gh332-x")));

        [Fact]
        public void UnrelatedRemains() => Assert.True(Directory.Exists(Path.Combine(root.Path, "unrelated-x")));
    }

    public sealed class ScenarioTheSweepRunsOncePerProcess
    {
        // A module initializer (xunit v2 has no assembly fixture) outside Specs/ and outside
        // TempSweep.cs itself calls TempSweep.Run on the real temp root.
        readonly string[] callers = Directory.EnumerateFiles(HostTestsDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Specs{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.EndsWith("TempSweep.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("TempSweep.Run(Path.GetTempPath()", StringComparison.Ordinal))
            .ToArray();

        [Fact]
        public void ExactlyOneInitializerCallsTheSweep() => Assert.Single(callers);
    }

    public sealed class ScenarioNoSpecBypassesTempDir
    {
        readonly string[] hits = Directory.EnumerateFiles(HostTestsDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains(Needle, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(HostTestsDir, f))
            .ToArray();

        [Fact]
        public void TheOnlyHitIsTempDir() =>
            Assert.Equal([Path.Combine("Support", "TempDir.cs")], hits);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDisposeSwallowsAVanishedDirectory
    {
        readonly Exception? caught;

        public ScenarioDisposeSwallowsAVanishedDirectory()
        {
            var dir = new TempDir();
            Directory.Delete(dir.Path, recursive: true);
            caught = Record.Exception(dir.Dispose);
        }

        [Fact]
        public void NoExceptionEscapes() => Assert.Null(caught);
    }
}
