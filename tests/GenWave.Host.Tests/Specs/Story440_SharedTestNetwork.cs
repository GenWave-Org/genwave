// STORY-440 — Test fixtures share one docker network (gh-#649 · SPEC F181.1 · PLAN T477)
//
// BDD specification — xUnit. AC3–AC5 drive TestNetwork.Ensure against a scripted `docker` on a
// scratch PATH (ScriptProcess.MakeBinDir/AddStub, gh-#776) — Ensure takes the executable path, so no
// process-wide PATH is ever touched. AC1/AC2/AC6 are text pins over the two fixture compose files
// and the two fixtures.
//
// RED at plan time: TestNetwork is a throwing skeleton; neither compose file names gw-test.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTestFixturesShareOneDockerNetwork
{
    static string RepoRoot => RepoRootLocator.Find(AppContext.BaseDirectory);
    static string ReadRepoFile(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot, .. parts]));

    /// <summary>A scratch bin dir with a `docker` stub that appends its argv to <paramref name="log"/>
    /// and answers `network create` with <paramref name="createExit"/>/<paramref name="createStderr"/>.</summary>
    static string StubDocker(string log, int createExit, string createStderr)
    {
        var bin = ScriptProcess.MakeBinDir();
        ScriptProcess.AddStub(bin, "docker", $"""
            printf '%s\n' "$*" >> "{log}"
            case "$*" in
              "network create "*) printf '%s\n' "{createStderr}" >&2; exit {createExit} ;;
            esac
            exit 0
            """);
        return Path.Combine(bin, "docker");
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheNetworkIsCreatedBeforeUp
    {
        readonly string[] calls;

        public ScenarioTheNetworkIsCreatedBeforeUp()
        {
            var log = Path.GetTempFileName();
            TestNetwork.Ensure(StubDocker(log, createExit: 0, createStderr: ""));
            calls = File.ReadAllLines(log);
        }

        [Fact]
        public void RunsExactlyOneNetworkCreate() =>
            Assert.Single(calls, c => c == $"network create {TestNetwork.Name}");
    }

    public sealed class ScenarioAlreadyExistsIsNotAnError
    {
        readonly Exception? caught;

        public ScenarioAlreadyExistsIsNotAnError()
        {
            var docker = StubDocker(Path.GetTempFileName(), createExit: 1,
                createStderr: "Error response from daemon: network with name gw-test already exists");
            caught = Record.Exception(() => TestNetwork.Ensure(docker));
        }

        [Fact]
        public void ReturnsWithoutThrowing() => Assert.Null(caught);
    }

    public sealed class ScenarioTheComposeFilesNameTheExternalNetwork
    {
        readonly string db = ReadRepoFile("tests", "GenWave.MediaLibrary.Tests", "db-compose.yaml");
        readonly string kokoro = ReadRepoFile("tests", "GenWave.Host.Tests", "kokoro-compose.yaml");

        [Fact]
        public void DbComposeDeclaresGwTestExternal() =>
            Assert.Matches(@"networks:\s*\n\s*default:\s*\n(\s+(name: gw-test|external: true)\s*\n){2}", db);

        [Fact]
        public void KokoroComposeDeclaresGwTestExternal() =>
            Assert.Matches(@"networks:\s*\n\s*default:\s*\n(\s+(name: gw-test|external: true)\s*\n){2}", kokoro);
    }

    public sealed class ScenarioBothFixturesCallEnsureBeforeComposeUp
    {
        readonly string databaseFixture = ReadRepoFile("tests", "GenWave.MediaLibrary.Tests", "DatabaseFixture.cs");
        readonly string kokoroFixture = ReadRepoFile("tests", "GenWave.Host.Tests", "KokoroFixture.cs");

        static bool EnsurePrecedesUp(string source)
        {
            var ensure = source.IndexOf("TestNetwork.Ensure(", StringComparison.Ordinal);
            var up = source.IndexOf("\"up\"", StringComparison.Ordinal);
            return ensure >= 0 && up >= 0 && ensure < up;
        }

        [Fact]
        public void DatabaseFixtureEnsuresFirst() => Assert.True(EnsurePrecedesUp(databaseFixture));

        [Fact]
        public void KokoroFixtureEnsuresFirst() => Assert.True(EnsurePrecedesUp(kokoroFixture));
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnyOtherFailureSurfaces
    {
        readonly Exception? caught;

        public ScenarioAnyOtherFailureSurfaces()
        {
            var docker = StubDocker(Path.GetTempFileName(), createExit: 1,
                createStderr: "permission denied while trying to connect to the Docker daemon socket");
            caught = Record.Exception(() => TestNetwork.Ensure(docker));
        }

        [Fact]
        public void ThrowsWithTheStderrText() =>
            Assert.Contains("permission denied", caught?.Message ?? "", StringComparison.Ordinal);
    }
}
