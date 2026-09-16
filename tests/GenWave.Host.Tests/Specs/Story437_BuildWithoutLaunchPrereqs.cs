// STORY-437 — build.sh builds on a box that cannot launch (gh-#775 · PLAN T470–T472)
//
// Runner: xUnit driving the REAL ./build.sh (and tools/preflight.sh directly) with scripted
// `dotnet`, `docker`, `git` and `ss` on PATH — the Gh019 scratch-bin idiom, via ScriptProcess
// (gh-#776), which always starts the child from a sanitized environment. Every run happens
// in a SCRATCH COPY of the repo root (symlinks, minus .env/.git) so "a fresh clone with no
// .env" is literally true on a dev box that has one, and a build can never create one in the
// tree. ScriptProcess.Run's fixed WorkingDirectory is harmless for RunScript since build.sh and
// launch.sh both self-relocate via `cd "$(dirname "$0")"` as their first meaningful action;
// CallPreflight sources tools/preflight.sh directly rather than running a script file, so it
// writes a scratch wrapper carrying the identical `cd "$(dirname "$0")"` self-relocation before
// sourcing, which reproduces the old WorkingDirectory=scratch behaviour without needing
// ScriptProcess.Run to support a custom working directory at all. The stubs log their argv and
// a snapshot of their environment so the specs can read what build.sh handed each tool.
//
// RED at plan time: build.sh runs the launch-grade `preflight_docker` (ports + disk + RAM) and
// `preflight_env_secrets`, so a clone without .env stops at "No .env file found" and a held
// port 8080 stops it before a single compile; `preflight_docker_build` does not exist yet.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureBuildWithoutLaunchPrerequisites
{
    // Each stub appends its argv to $GW_TOOL_LOG and dumps its environment to
    // $GW_TOOL_LOG.<tool>.<first-arg>.env — `docker compose build ...` lands in
    // docker.compose.env, `dotnet test ...` in dotnet.test.env.
    const string LogAndDumpEnv = """
        printf '%s %s\n' "$(basename "$0")" "$*" >> "$GW_TOOL_LOG"
        env > "$GW_TOOL_LOG.$(basename "$0").${1:-none}.env"
        """;

    const string DotnetStub = LogAndDumpEnv + """

        case "${1:-}" in
          --list-sdks) echo "10.0.100 [/usr/share/dotnet/sdk]"; exit 0 ;;
        esac
        exit 0
        """;

    const string DockerStubHealthy = LogAndDumpEnv + """

        case "${1:-}" in
          info) exit 0 ;;
        esac
        case "$*" in
          *" version"*) echo "Docker Compose version v2.29.0"; exit 0 ;;
        esac
        exit 0
        """;

    const string DockerStubOldCompose = LogAndDumpEnv + """

        case "${1:-}" in
          info) exit 0 ;;
        esac
        case "$*" in
          *" version"*) echo "Docker Compose version v2.20.3"; exit 0 ;;
        esac
        exit 0
        """;

    const string GitStub = """
        echo v5.8.2-spec
        """;

    // Port 8080 held by a local process — a launch must refuse this, a build must not care.
    const string SsHolding8080 = """
        cat <<'OUT'
        State   Recv-Q  Send-Q   Local Address:Port   Peer Address:Port  Process
        LISTEN  0       128            0.0.0.0:8080        0.0.0.0:*      users:(("python3",pid=4242,fd=3))
        OUT
        """;

    static string MakeBinDir(string dockerStub = DockerStubHealthy, bool withDocker = true)
    {
        var dir = ScriptProcess.MakeBinDir("basename");
        ScriptProcess.AddStub(dir, "dotnet", DotnetStub);
        ScriptProcess.AddStub(dir, "git", GitStub);
        ScriptProcess.AddStub(dir, "ss", SsHolding8080);
        if (withDocker) ScriptProcess.AddStub(dir, "docker", dockerStub);
        return dir;
    }

    /// <summary>A fresh clone: every top-level entry of the repo, minus .env and .git.</summary>
    static string MakeScratchRepo()
    {
        var root = RepoRootLocator.Find(AppContext.BaseDirectory);
        var scratch = TempDir.CreateForProcessLifetime();
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var name = Path.GetFileName(entry);
            if (name is ".env" or ".git")
                continue;
            if (Directory.Exists(entry))
                Directory.CreateSymbolicLink(Path.Combine(scratch, name), entry);
            else
                File.CreateSymbolicLink(Path.Combine(scratch, name), entry);
        }
        return scratch;
    }

    sealed record Run(int ExitCode, string StdOut, string StdErr, string Scratch, string ToolLog)
    {
        public string[] ToolCalls => File.Exists(ToolLog) ? File.ReadAllLines(ToolLog) : [];

        /// <summary>The environment a stub saw, as NAME → value, for `<tool> <first-arg>`.</summary>
        public IReadOnlyDictionary<string, string> EnvSeenBy(string tool, string firstArg)
        {
            var path = $"{ToolLog}.{tool}.{firstArg}.env";
            if (!File.Exists(path)) return new Dictionary<string, string>();
            return File.ReadAllLines(path)
                .Select(l => l.Split('=', 2))
                .Where(kv => kv.Length == 2)
                .GroupBy(kv => kv[0])
                .ToDictionary(g => g.Key, g => g.First()[1]);
        }
    }

    static Run RunScript(string script, string bin, IReadOnlyDictionary<string, string>? extraEnv = null, params string[] args)
    {
        var scratch = MakeScratchRepo();
        var log = Path.Combine(TempDir.CreateForProcessLifetime(), "tools.log");

        var mergedEnv = new Dictionary<string, string> { ["GW_TOOL_LOG"] = log };
        if (extraEnv is not null)
            foreach (var (name, value) in extraEnv)
                mergedEnv[name] = value;

        var (exitCode, stdOut, stdErr) = ScriptProcess.Run(
            Path.Combine(scratch, script), bin, extraEnv: mergedEnv, args: args);

        return new Run(exitCode, stdOut, stdErr, scratch, log);
    }

    /// <summary>Sources tools/preflight.sh in the scratch repo and calls one function — the
    /// Story342 idiom. ScriptProcess.Run only knows how to run a script FILE, not `bash -c`, so
    /// this writes a one-line-longer scratch wrapper carrying the same self-relocating
    /// `cd "$(dirname "$0")"` every real script in this repo opens with — sourcing
    /// tools/preflight.sh through it reaches the scratch repo's own copy exactly as the old
    /// WorkingDirectory=scratch invocation did.</summary>
    static Run CallPreflight(string function, string bin)
    {
        var scratch = MakeScratchRepo();
        var log = Path.Combine(scratch, "tools.log");

        var wrapperPath = Path.Combine(scratch, "call-preflight.sh");
        File.WriteAllText(wrapperPath, $$"""
            #!/usr/bin/env bash
            set -euo pipefail
            cd "$(dirname "$0")"
            . tools/preflight.sh
            {{function}}
            """);

        var (exitCode, stdOut, stdErr) = ScriptProcess.Run(
            wrapperPath, bin, extraEnv: new Dictionary<string, string> { ["GW_TOOL_LOG"] = log });

        return new Run(exitCode, stdOut, stdErr, scratch, log);
    }

    static string WriteEnvFile()
    {
        var path = Path.Combine(TempDir.CreateForProcessLifetime(), "test.env");
        File.WriteAllLines(path,
        [
            "POSTGRES_PASSWORD=x", "LIBRARY_DB_PASSWORD=x", "STATION_DB_PASSWORD=x",
            "ICECAST_SOURCE_PASSWORD=x", "ICECAST_ADMIN_PASSWORD=x",
            $"MEDIA_DIR={Path.GetTempPath()}",
        ]);
        return path;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — a fresh clone builds
    // ---------------------------------------------------------------------

    public static class ScenarioAFreshCloneWithNoEnvFileAndAHeldPort
    {
        static readonly Lazy<Run> Build = new(() => RunScript("build.sh", MakeBinDir()));

        [Fact]
        public static void The_build_succeeds()
            => Assert.Equal(0, Build.Value.ExitCode);

        [Fact]
        public static void The_solution_is_compiled()
            => Assert.Contains(Build.Value.ToolCalls, c => c.StartsWith("dotnet build ", StringComparison.Ordinal));

        [Fact]
        public static void The_tests_are_run()
            => Assert.Contains(Build.Value.ToolCalls, c => c.StartsWith("dotnet test ", StringComparison.Ordinal));

        [Fact]
        public static void The_images_are_built()
            => Assert.Contains(Build.Value.ToolCalls, c => c.StartsWith("docker compose build ", StringComparison.Ordinal));

        [Fact]
        public static void The_held_port_is_not_mentioned()
            // Ports are a LAUNCH concern; a compile needs none of them.
            => Assert.DoesNotContain("Port 8080", Build.Value.StdErr);

        [Fact]
        public static void No_env_file_is_created()
            => Assert.False(File.Exists(Path.Combine(Build.Value.Scratch, ".env")));

        [Fact]
        public static void Nothing_is_brought_up_or_down()
            => Assert.DoesNotContain(Build.Value.ToolCalls,
                c => c.Contains(" up", StringComparison.Ordinal) || c.Contains(" down", StringComparison.Ordinal));

        // compose.yaml's ${VAR:?} guards fail a render three tools deep without a value; the
        // build exports a placeholder for each one it did not inherit (ruling 2026-09-15).
        [Fact]
        public static void The_image_build_sees_a_postgres_password_placeholder()
            => Assert.Equal("build-only", Build.Value.EnvSeenBy("docker", "compose").GetValueOrDefault("POSTGRES_PASSWORD"));

        [Fact]
        public static void The_image_build_sees_a_library_db_password_placeholder()
            => Assert.Equal("build-only", Build.Value.EnvSeenBy("docker", "compose").GetValueOrDefault("LIBRARY_DB_PASSWORD"));

        [Fact]
        public static void The_image_build_sees_a_station_db_password_placeholder()
            => Assert.Equal("build-only", Build.Value.EnvSeenBy("docker", "compose").GetValueOrDefault("STATION_DB_PASSWORD"));

        [Fact]
        public static void The_image_build_sees_an_icecast_source_password_placeholder()
            => Assert.Equal("build-only", Build.Value.EnvSeenBy("docker", "compose").GetValueOrDefault("ICECAST_SOURCE_PASSWORD"));

        [Fact]
        public static void The_image_build_sees_an_icecast_admin_password_placeholder()
            => Assert.Equal("build-only", Build.Value.EnvSeenBy("docker", "compose").GetValueOrDefault("ICECAST_ADMIN_PASSWORD"));

        [Fact]
        // MEDIA_DIR is a bind-mount SOURCE (`${MEDIA_DIR:?}:/media:ro`): compose reads a bare word
        // there as a named volume and refuses the project, so its placeholder is path-shaped.
        public static void The_image_build_sees_a_media_dir_placeholder()
            => Assert.Equal("/build-only", Build.Value.EnvSeenBy("docker", "compose").GetValueOrDefault("MEDIA_DIR"));
    }

    public static class ScenarioTheParentShellAlreadyHasARealValue
    {
        static readonly Lazy<Run> Build = new(() => RunScript("build.sh", MakeBinDir(),
            new Dictionary<string, string> { ["POSTGRES_PASSWORD"] = "real-secret-from-ci" }));

        [Fact]
        public static void The_build_succeeds()
            => Assert.Equal(0, Build.Value.ExitCode);

        [Fact]
        public static void The_real_value_is_not_overwritten_by_the_placeholder()
            => Assert.Equal("real-secret-from-ci", Build.Value.EnvSeenBy("docker", "compose").GetValueOrDefault("POSTGRES_PASSWORD"));

        [Fact]
        public static void The_other_secrets_still_get_placeholders()
            => Assert.Equal("/build-only", Build.Value.EnvSeenBy("docker", "compose").GetValueOrDefault("MEDIA_DIR"));
    }

    public static class ScenarioTheBuildIsRunWithPreflightSkipped
    {
        // A CI runner or a hurried developer exports SKIP_PREFLIGHT=1 for build.sh itself; the
        // Host suite it runs contains Gh019/Story342, whose whole subject is preflight — they
        // must not inherit the skip (gh-#776's sibling symptom, fixed on the build side here).
        static readonly Lazy<Run> Build = new(() => RunScript("build.sh", MakeBinDir(),
            new Dictionary<string, string> { ["SKIP_PREFLIGHT"] = "1", ["SKIP_TESTS"] = "0" }));

        [Fact]
        public static void The_build_succeeds()
            => Assert.Equal(0, Build.Value.ExitCode);

        [Fact]
        public static void The_test_run_does_not_inherit_skip_preflight()
            => Assert.False(Build.Value.EnvSeenBy("dotnet", "test").ContainsKey("SKIP_PREFLIGHT"));

        [Fact]
        public static void The_test_run_does_not_inherit_skip_tests()
            => Assert.False(Build.Value.EnvSeenBy("dotnet", "test").ContainsKey("SKIP_TESTS"));
    }

    public static class ScenarioTheBuildPreflightIsTheDockerSubsetOnly
    {
        static readonly Lazy<Run> Call = new(() => CallPreflight("preflight_docker_build", MakeBinDir()));

        [Fact]
        public static void A_held_port_passes_the_build_preflight()
            => Assert.Equal(0, Call.Value.ExitCode);

        [Fact]
        public static void No_port_row_is_recorded()
            => Assert.DoesNotContain("Port", Call.Value.StdOut + Call.Value.StdErr);
    }

    public static class ScenarioTheLaunchPreflightStillChecksPorts
    {
        // The full set stays on the launch path: the port 8080 the build ignores is still a
        // hard stop for ./launch.sh (Gh019/Story342's contract, restated here as the boundary).
        static readonly Lazy<Run> Launch = new(() => RunScript("launch.sh", MakeBinDir(),
            new Dictionary<string, string> { ["GW_ENV_FILE"] = WriteEnvFile() }));

        [Fact]
        public static void The_launch_refuses()
            => Assert.NotEqual(0, Launch.Value.ExitCode);

        [Fact]
        public static void The_launch_names_the_port()
            => Assert.Contains("Port 8080", Launch.Value.StdErr);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the build preflight is a subset, not a bypass
    // ---------------------------------------------------------------------

    public static class ScenarioTheBuildPreflightRejectsAMachineWithoutDocker
    {
        static readonly Lazy<Run> Call = new(() => CallPreflight("preflight_docker_build", MakeBinDir(withDocker: false)));

        [Fact]
        public static void The_call_fails()
            => Assert.NotEqual(0, Call.Value.ExitCode);

        [Fact]
        public static void The_failure_says_docker_is_missing()
            => Assert.Contains("Docker is not installed", Call.Value.StdErr);
    }

    public static class ScenarioTheBuildPreflightRejectsAnOldComposePlugin
    {
        static readonly Lazy<Run> Call = new(() => CallPreflight("preflight_docker_build", MakeBinDir(DockerStubOldCompose)));

        [Fact]
        public static void The_call_fails()
            => Assert.NotEqual(0, Call.Value.ExitCode);

        [Fact]
        public static void The_failure_names_the_floor()
            => Assert.Contains("older than the v2.24", Call.Value.StdErr);
    }
}
