// STORY-441 — Test temp directories clean up after themselves (gh-#710 · SPEC F181.2 · PLAN T479/T480)
//
// BDD specification — xUnit. AC1–AC3 drive TempDir; AC5–AC7 drive TempSweep.Run over a scratch root
// (never the real temp root); AC4 and AC8 are source pins over tests/GenWave.Host.Tests.
//
// RED at plan time: TempDir/TempSweep are throwing skeletons; 28 files still call
// Directory.CreateTempSubdirectory directly.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTestTempDirectoriesCleanUpAfterThemselves
{
    const string PendingType = "pending: T479 — TempDir + TempSweep + the once-per-process sweep (STORY-441)";
    const string PendingCallSites = "pending: T480 — every CreateTempSubdirectory call site moves onto TempDir (STORY-441)";

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

        [Fact(Skip = PendingType)]
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

        [Fact(Skip = PendingType)]
        public void TheDirectoryIsGone() => Assert.False(Directory.Exists(path));
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — TempSweep
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheSweepRemovesStaleAndKeepsFresh
    {
        readonly string root;

        public ScenarioTheSweepRemovesStaleAndKeepsFresh()
        {
            root = Directory.CreateTempSubdirectory("story441-root-").FullName;
            var stale = DateTime.UtcNow - TimeSpan.FromHours(2);
            foreach (var name in new[] { "gw-stale", "genwave-pawire-x", "story343-env-x", "gh332-x", "unrelated-x" })
            {
                var dir = Directory.CreateDirectory(Path.Combine(root, name));
                Directory.SetLastWriteTimeUtc(dir.FullName, stale);
            }
            Directory.CreateDirectory(Path.Combine(root, "gw-fresh"));

            TempSweep.Run(root, olderThan: TimeSpan.FromHours(1));
        }

        [Fact(Skip = PendingType)]
        public void GwStaleIsGone() => Assert.False(Directory.Exists(Path.Combine(root, "gw-stale")));

        [Fact(Skip = PendingType)]
        public void GwFreshRemains() => Assert.True(Directory.Exists(Path.Combine(root, "gw-fresh")));

        [Fact(Skip = PendingType)]
        public void PawireIsGone() => Assert.False(Directory.Exists(Path.Combine(root, "genwave-pawire-x")));

        [Fact(Skip = PendingType)]
        public void Story343EnvIsGone() => Assert.False(Directory.Exists(Path.Combine(root, "story343-env-x")));

        [Fact(Skip = PendingType)]
        public void Gh332IsGone() => Assert.False(Directory.Exists(Path.Combine(root, "gh332-x")));

        [Fact(Skip = PendingType)]
        public void UnrelatedRemains() => Assert.True(Directory.Exists(Path.Combine(root, "unrelated-x")));
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

        [Fact(Skip = PendingType)]
        public void ExactlyOneInitializerCallsTheSweep() => Assert.Single(callers);
    }

    public sealed class ScenarioNoSpecCallsCreateTempSubdirectoryDirectly
    {
        readonly string[] hits = Directory.EnumerateFiles(HostTestsDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("CreateTempSubdirectory", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(HostTestsDir, f))
            .ToArray();

        [Fact(Skip = PendingCallSites)]
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

        [Fact(Skip = PendingType)]
        public void NoExceptionEscapes() => Assert.Null(caught);
    }
}
